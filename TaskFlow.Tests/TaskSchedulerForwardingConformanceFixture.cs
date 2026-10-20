namespace TaskFlow.Tests;

using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using System.Threading.Tasks.Flow;

[TestFixture]
[CancelAfter(10000)]
internal sealed class TaskSchedulerForwardingConformanceFixture
{
    [TestCase("Annotated", "disposed", "DirectGeneric")]
    [TestCase("Forwarding", "disposed", "DirectGeneric")]
    [TestCase("Execution", "disposed", "Task")]
    [TestCase("Completion", "disposed", "Action")]
    [TestCase("Nested", "disposed", "ValueTask")]
    [TestCase("Owned", "disposed", "DirectGeneric")]
    [TestCase("Throttle", "disposed", "DirectGeneric")]
    [TestCase("CancellationScope", "disposed", "DirectGeneric")]
    [TestCase("CancelPrevious", "disposed", "DirectGeneric")]
    [TestCase("Forwarding", "argument", "Action")]
    [TestCase("Forwarding", "failure", "Task")]
    [TestCase("Forwarding", "cancellation", "ValueTask")]
    public async Task Enqueue_ImmediateTerminalRejection_ShouldThrowSynchronously(
        string configuration, string failureType, string overload)
    {
        var failure = CreateFailure(failureType);
        var terminal = new Terminal(failure, throwSynchronously: true);
        await using var lifetime = new TaskSchedulerMiddlewareLifetime();
        using var observer = new Observer();
        var scheduler = Configure(terminal, configuration, lifetime, observer);
        Task? returned = null;

        Assert.That(() => { returned = TaskSchedulerDisposalConformance.Enqueue(scheduler, overload, () => { }, false); }, Throws.Exception.SameAs(failure));
        Assert.That(returned, Is.Null);
        Assert.That(terminal.Calls, Is.EqualTo(1));
        Assert.That(observer.Completions, Is.Zero, "Immediate scheduling rejection must reach the caller before a task is returned.");

        await lifetime.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        if (configuration == "Owned")
            Assert.That(observer.Disposed, Is.True, "Rejected forwarding must release the pipeline's lifetime entry.");
    }

    [TestCase("Forwarding", "disposed", "DirectGeneric")]
    [TestCase("Completion", "disposed", "Action")]
    [TestCase("Owned", "argument", "DirectGeneric")]
    [TestCase("CancellationScope", "failure", "Task")]
    [TestCase("CancelPrevious", "cancellation", "ValueTask")]
    [TestCase("Timeout", "disposed", "DirectGeneric")]
    public async Task Enqueue_ReturnedFailureTask_ShouldStayTaskBased(
        string configuration, string failureType, string overload)
    {
        var failure = CreateFailure(failureType);
        var terminal = new Terminal(failure, throwSynchronously: false);
        await using var lifetime = new TaskSchedulerMiddlewareLifetime();
        using var observer = new Observer();
        var scheduler = Configure(terminal, configuration, lifetime, observer);
        Task? returned = null;

        Assert.That(() => { returned = TaskSchedulerDisposalConformance.Enqueue(scheduler, overload, () => { }, false); }, Throws.Nothing);
        await Assert.ThatAsync(async () => await returned!, Throws.Exception.SameAs(failure));
        Assert.That(terminal.Calls, Is.EqualTo(1));
        Assert.That(observer.Completions, Is.EqualTo(configuration is "Completion" or "Owned" ? 1 : 0));
    }

    [Test]
    public async Task Enqueue_Timeout_ShouldReportImmediateTerminalRejectionThroughTask()
    {
        var failure = new ObjectDisposedException("inner");
        var terminal = new Terminal(failure, throwSynchronously: true);
        using var observer = new Observer();
        var scheduler = terminal.UseMiddleware((ITaskSchedulerCompletionMiddleware)observer).WithTimeout(TimeSpan.FromHours(1));
        Task? returned = null;

        Assert.That(() => { returned = scheduler.Enqueue(() => 42); }, Throws.Nothing);
        await Assert.ThatAsync(async () => await returned!, Throws.Exception.SameAs(failure));
        Assert.That(terminal.Calls, Is.EqualTo(1));
        Assert.That(observer.Completions, Is.EqualTo(1));
    }

    [TestCase("CancellationScope", true)]
    [TestCase("CancellationScope", false)]
    [TestCase("CancelPrevious", true)]
    [TestCase("CancelPrevious", false)]
    public async Task Enqueue_CancellationResources_ShouldLiveUntilCompletionOrSynchronousRejection(string configuration, bool reject)
    {
        var failure = new ObjectDisposedException("inner");
        var terminal = new TokenCapturingTerminal(failure, reject);
        using var caller = new CancellationTokenSource();
        var scheduler = configuration == "CancellationScope"
            ? terminal.CreateCancellationScope(CancellationToken.None)
            : terminal.CreateCancelPrevious();
        try
        {
            if (reject)
                Assert.That(() => scheduler.Enqueue((_, _) => new ValueTask<int>(42), null, caller.Token), Throws.Exception.SameAs(failure));
            else
            {
                var pending = scheduler.Enqueue((_, _) => new ValueTask<int>(42), null, caller.Token);
                Assert.That(() => terminal.Token.WaitHandle, Throws.Nothing, "Accepted work must retain its linked source.");
                await caller.CancelAsync();
                Assert.That(terminal.Token.IsCancellationRequested, Is.True);
                terminal.Release.SetResult();
                await pending;
            }
            Assert.That(() => terminal.Token.WaitHandle, Throws.TypeOf<ObjectDisposedException>(), "The linked source must be released.");
        }
        finally { terminal.Release.TrySetResult(); }
    }

    [Test]
    public async Task Enqueue_ShortCircuit_ShouldNotProbeOrCallRejectedTerminal()
    {
        var terminal = new Terminal(new ObjectDisposedException("inner"), throwSynchronously: true);
        var scheduler = terminal.UseMiddleware(new ConstantAdmission());
        Assert.That(await scheduler.Enqueue(() => 42), Is.EqualTo(17));
        Assert.That(terminal.Calls, Is.Zero);
    }

    [Test]
    public async Task Enqueue_DeferredForwarding_ShouldReportLaterTerminalRejectionThroughTask()
    {
        var failure = new ObjectDisposedException("inner");
        var terminal = new Terminal(failure, throwSynchronously: true);
        var admission = new DeferredAdmission();
        using var observer = new Observer();
        var scheduler = terminal.UseMiddleware((ITaskSchedulerCompletionMiddleware)observer).UseMiddleware(admission);
        Task? returned = null;
        Assert.That(() => { returned = scheduler.Enqueue((_, _) => new ValueTask<int>(42), null, CancellationToken.None); }, Throws.Nothing);
        Assert.That(returned!.IsCompleted, Is.False);
        Assert.That(terminal.Calls, Is.Zero);

        admission.Release.SetResult();
        await Assert.ThatAsync(async () => await returned, Throws.Exception.SameAs(failure));
        Assert.That(terminal.Calls, Is.EqualTo(1));
        Assert.That(observer.Completions, Is.EqualTo(1));
    }

    [Test]
    public async Task Enqueue_ClosedOwnLifetime_ShouldRejectWithoutCallingAvailableTerminal()
    {
        var terminal = new Terminal(new InvalidOperationException("terminal must not be reached"), throwSynchronously: true);
        await using var lifetime = new TaskSchedulerMiddlewareLifetime();
        using var observer = new Observer();
        var scheduler = terminal.UseMiddleware(new Factory(observer), lifetime);
        await lifetime.DisposeAsync();
        Assert.That(() => scheduler.Enqueue(() => 42), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(terminal.Calls, Is.Zero);
        Assert.That(observer.Forwards, Is.Zero);
        Assert.That(observer.Disposed, Is.True);
    }

    [Test]
    public async Task Enqueue_SynchronousFailureAfterForwarding_ShouldRetainAcceptedChildUntilQuiescent()
    {
        await using var lifetime = new TaskSchedulerMiddlewareLifetime();
        using var observer = new Observer();
        var failure = new InvalidOperationException("after forwarding");
        var forwarding = new ThrowAfterForward(failure);
        var scheduler = new TaskFlowSchedulerAdapter(TaskScheduler.Default)
            .UseMiddleware(new Factory(observer), lifetime).UseMiddleware(forwarding);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async ValueTask<int> Work(object? state, CancellationToken token)
        {
            entered.SetResult();
            await release.Task.ConfigureAwait(false);
            return 42;
        }
        try
        {
            Assert.That(() => scheduler.Enqueue(Work, null, CancellationToken.None), Throws.Exception.SameAs(failure));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var shutdown = lifetime.DisposeAsync().AsTask();
            Assert.That(shutdown.IsCompleted, Is.False);
            Assert.That(observer.Disposed, Is.False);
            release.SetResult();
            await forwarding.Child!.WaitAsync(TimeSpan.FromSeconds(5));
            await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(observer.Completions, Is.EqualTo(1));
            Assert.That(observer.Disposed, Is.True);
        }
        finally
        {
            release.TrySetResult();
            if (forwarding.Child != null)
                await forwarding.Child.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    public async Task Enqueue_Timeout_ShouldIncludeTimeSpentForwarding()
    {
        var clock = new FakeTimeProvider();
        var terminal = new TimeAdvancingTerminal(clock);
        var scheduler = terminal.WithTimeout(TimeSpan.FromSeconds(30), clock);
        try
        {
            var task = scheduler.Enqueue(() => 42);
            await Assert.ThatAsync(async () => await task.WaitAsync(TimeSpan.FromSeconds(5)),
                Throws.TypeOf<TimeoutException>().With.Message.Contains("Operation has timed out in"));
        }
        finally { terminal.Release.TrySetResult(); }
    }

    [Test]
    public async Task Enqueue_Timeout_ShouldRetainAcceptedExecutionUntilLifetimeDrains()
    {
        await using var flow = new TaskFlow();
        using var observer = new Observer();
        var clock = new FakeTimeProvider();
        var scheduler = flow.UseMiddleware(new Factory(observer)).WithTimeout(TimeSpan.FromSeconds(30), clock);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var task = scheduler.Enqueue(async (_, _) =>
            {
                entered.SetResult();
                await release.Task.ConfigureAwait(false);
                return 42;
            }, null, CancellationToken.None);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock.Advance(TimeSpan.FromSeconds(31));
            await Assert.ThatAsync(async () => await task, Throws.TypeOf<TimeoutException>());
            Assert.That(observer.Completions, Is.EqualTo(1));
            var shutdown = flow.DisposeAsync().AsTask();
            Assert.That(shutdown.IsCompleted, Is.False);
            Assert.That(observer.Disposed, Is.False);
            release.SetResult();
            await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(observer.Completions, Is.EqualTo(1));
            Assert.That(observer.Disposed, Is.True);
        }
        finally
        {
            release.TrySetResult();
            await flow.DisposeAsync();
        }
    }

    private static ITaskScheduler Configure(ITaskScheduler terminal, string configuration,
        TaskSchedulerMiddlewareLifetime lifetime, Observer observer)
        => configuration switch
        {
            "Annotated" => terminal.WithOperationName("forwarding"),
            "Forwarding" => terminal.UseMiddleware(new Forwarding()).UseMiddleware(new Forwarding()),
            "Execution" => terminal.UseMiddleware(new Execution()),
            "Completion" => terminal.UseMiddleware((ITaskSchedulerCompletionMiddleware)observer),
            "Nested" => new Opaque(terminal.UseMiddleware(new Forwarding())).UseMiddleware(new Forwarding()),
            "Owned" => terminal.UseMiddleware(new Factory(observer), lifetime),
            "Throttle" => terminal.WithThrottle(TimeSpan.FromHours(1)),
            "CancellationScope" => terminal.CreateCancellationScope(CancellationToken.None),
            "CancelPrevious" => terminal.CreateCancelPrevious(),
            "Timeout" => terminal.WithTimeout(TimeSpan.FromHours(1)),
            _ => throw new ArgumentOutOfRangeException(nameof(configuration)),
        };

    private static Exception CreateFailure(string failureType)
        => failureType switch
        {
            "disposed" => new ObjectDisposedException("inner"),
            "argument" => new ArgumentException("rejected"),
            "failure" => new InvalidOperationException("rejected"),
            "cancellation" => new OperationCanceledException(new CancellationToken(canceled: true)),
            _ => throw new ArgumentOutOfRangeException(nameof(failureType)),
        };

    private sealed class Terminal(Exception failure, bool throwSynchronously) : ITaskScheduler
    {
        public int Calls { get; private set; }
        public Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
        {
            Calls++;
            if (throwSynchronously)
                throw failure;
            return Task.FromException<T>(failure);
        }
    }

    private sealed class TokenCapturingTerminal(Exception failure, bool reject) : ITaskScheduler
    {
        public CancellationToken Token { get; private set; }
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            if (reject)
                throw failure;
            return AwaitRelease<T>();
        }
        private async Task<T> AwaitRelease<T>()
        {
            await Release.Task.ConfigureAwait(false);
            return default!;
        }
    }

    private sealed class TimeAdvancingTerminal(FakeTimeProvider clock) : ITaskScheduler
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
        {
            clock.Advance(TimeSpan.FromSeconds(31));
            return AwaitRelease<T>();
        }
        private async Task<T> AwaitRelease<T>()
        {
            await Release.Task.ConfigureAwait(false);
            return default!;
        }
    }

    private sealed class Opaque(ITaskScheduler inner) : ITaskScheduler
    {
        public Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
            => inner.Enqueue(taskFunc, state, cancellationToken);
    }

    private sealed class Forwarding : ITaskSchedulerEnqueueMiddleware
    {
        public Task<T> InvokeAsync<T>(TaskSchedulerEnqueueContext<T> context, TaskSchedulerEnqueueDelegate<T> next) => next(context);
    }

    private sealed class Execution : ITaskSchedulerExecutionMiddleware
    {
        public ValueTask<T> InvokeAsync<T>(TaskSchedulerOperationContext context, TaskSchedulerExecutionDelegate<T> next) => next(context);
    }

    private sealed class ConstantAdmission : ITaskSchedulerEnqueueMiddleware
    {
        public Task<T> InvokeAsync<T>(TaskSchedulerEnqueueContext<T> context, TaskSchedulerEnqueueDelegate<T> next)
            => Task.FromResult((T)(object)17);
    }

    private sealed class ThrowAfterForward(Exception failure) : ITaskSchedulerEnqueueMiddleware
    {
        public Task? Child { get; private set; }
        public Task<T> InvokeAsync<T>(TaskSchedulerEnqueueContext<T> context, TaskSchedulerEnqueueDelegate<T> next)
        {
            Child = next(context);
            throw failure;
        }
    }

    private sealed class DeferredAdmission : ITaskSchedulerEnqueueMiddleware
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<T> InvokeAsync<T>(TaskSchedulerEnqueueContext<T> context, TaskSchedulerEnqueueDelegate<T> next)
        {
            await Release.Task.ConfigureAwait(false);
            return await next(context).ConfigureAwait(false);
        }
    }

    private sealed class Observer : ITaskSchedulerEnqueueMiddleware, ITaskSchedulerCompletionMiddleware, IDisposable
    {
        public int Forwards { get; private set; }
        public int Completions { get; private set; }
        public bool Disposed { get; private set; }
        public Task<T> InvokeAsync<T>(TaskSchedulerEnqueueContext<T> context, TaskSchedulerEnqueueDelegate<T> next)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            Forwards++;
            return next(context);
        }
        public ValueTask<TaskSchedulerOperationOutcome<T>> InvokeAsync<T>(TaskSchedulerOperationContext context,
            TaskSchedulerOperationOutcome<T> outcome, TaskSchedulerCompletionDelegate<T> next)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            Completions++;
            return next(context, outcome);
        }
        public void Dispose() => Disposed = true;
    }

    private sealed class Factory(Observer observer) : ITaskSchedulerMiddlewareFactory
    {
        public ITaskSchedulerMiddleware Create(TaskSchedulerMiddlewareLifetime lifetime) => observer;
    }
}