namespace TaskFlow.Tests.Extensions;

using NUnit.Framework;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks.Flow;

[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Factories transfer cleanup to the tested lifetime; caller-owned test middleware holds only observation state.")]
[TestFixture]
[CancelAfter(10000)]
internal sealed class CompletionContinuationLifetimeFixture
{
    private static readonly string[] CleanupOrder = ["cleanup:inner", "cleanup:outer"];
    private static readonly string[] FinishAndCleanupOrder = ["finish:inner", "cleanup:inner", "cleanup:outer"];

    private static TaskCompletionSource<bool> Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static IEnumerable<TestCaseData> IndependentCases()
    {
        yield return new(false, false, 1, 1, "success");
        yield return new(true, false, 1, 1, "success");
        yield return new(false, true, 1, 1, "success");
        yield return new(false, false, 3, 1, "success");
        yield return new(false, false, 1, 2, "success");
        yield return new(false, false, 1, 1, "fault");
        yield return new(false, false, 1, 1, "cancel");
    }

    [TestCaseSource(nameof(IndependentCases))]
    public async Task IndependentChildren_DrainBeforeOwnedCleanup(bool explicitLifetime, bool throwAfterNext,
        int layers, int calls, string childOutcome)
    {
        var events = new ConcurrentQueue<string>();
        var flow = new TaskFlow();
        var lifetime = new TaskSchedulerMiddlewareLifetime();
        var outer = new IndependentCompletion(events) { Calls = calls, ThrowAfterNext = throwAfterNext };
        using var producer = new CancellationTokenSource();
        using var replacement = new CancellationTokenSource();
        await replacement.CancelAsync();
        outer.ReplacementToken = replacement.Token;
        var inner = new GatedCompletion(events)
        {
            Failure = childOutcome switch
            {
                "fault" => new FormatException("child"),
                "cancel" => new OperationCanceledException(replacement.Token),
                _ => null
            }
        };
        ITaskScheduler scheduler = explicitLifetime
            ? new TaskFlowSchedulerAdapter(TaskScheduler.Default).UseMiddleware(new Factory(outer), lifetime)
            : flow.UseMiddleware(new Factory(outer));
        for (var i = 1; i < layers; i++)
            scheduler = scheduler.UseMiddleware(new IndependentCompletion(events));
        scheduler = scheduler.UseMiddleware(new Factory(inner));
        var operation = scheduler.Enqueue((_, _) => new ValueTask<int>(42), null, producer.Token);
        try
        {
            await inner.Entered.Task;
            if (throwAfterNext)
                await Assert.ThatAsync(async () => await operation, Throws.Exception.SameAs(outer.Failure));
            else
                Assert.That(await operation, Is.EqualTo(17));
            Assert.That(outer.Children, Has.Count.EqualTo(calls));
            Assert.That(inner.Context!.CancellationToken, Is.EqualTo(replacement.Token));
            Assert.That(inner.Context.ProducerCancellationToken, Is.EqualTo(producer.Token));
            Assert.That(inner.Context.CallerCancellationToken, Is.EqualTo(producer.Token));
            Assert.That(outer.Context!.CancellationToken, Is.Not.EqualTo(replacement.Token));
            var shutdown = explicitLifetime ? lifetime.DisposeAsync().AsTask() : flow.DisposeAsync().AsTask();
            Assert.That(shutdown.IsCompleted, Is.False);
            Assert.That(events, Does.Not.Contain("cleanup:inner"));
            Assert.That(events, Does.Not.Contain("cleanup:outer"));
            inner.Release.SetResult(true);
            await Task.WhenAll(outer.Children);
            await shutdown;
            Assert.That(inner.Calls, Is.EqualTo(calls));
            Assert.That(inner.Finished, Is.EqualTo(calls));
            Assert.That(events.ToArray()[^2..], Is.EqualTo(CleanupOrder));
            if (layers == 1)
                Assert.That(outer.ChildFailures, Is.All.SameAs(inner.Failure));
            if (!throwAfterNext)
                Assert.That(await operation, Is.EqualTo(17));
        }
        finally
        {
            inner.Release.TrySetResult(true);
            await lifetime.DisposeAsync();
            await flow.DisposeAsync();
        }
    }

    [Test]
    public async Task ReturnedSubmissionFailure_IndependentCompletionStillDrains()
    {
        var events = new ConcurrentQueue<string>();
        await using var lifetime = new TaskSchedulerMiddlewareLifetime();
        var outer = new IndependentCompletion(events);
        var inner = new GatedCompletion(events);
        var scheduler = new FaultedTerminal().UseMiddleware(new Factory(outer), lifetime)
            .UseMiddleware(new Factory(inner));
        var operation = scheduler.Enqueue(() => 42);
        try
        {
            await inner.Entered.Task;
            Assert.That(await operation, Is.EqualTo(17));
            var shutdown = lifetime.DisposeAsync().AsTask();
            Assert.That(shutdown.IsCompleted, Is.False);
            Assert.That(events, Is.Empty);
            inner.Release.SetResult(true);
            await Task.WhenAll(outer.Children);
            await shutdown;
            Assert.That(outer.ChildFailures.Single(), Is.TypeOf<FormatException>());
            Assert.That(inner.Calls, Is.EqualTo(1));
            Assert.That(events.ToArray(), Is.EqualTo(FinishAndCleanupOrder));
        }
        finally { inner.Release.TrySetResult(true); }
    }

    [Test]
    public async Task ChildFinishesFirst_ParentStillPinsOperation()
    {
        var events = new ConcurrentQueue<string>();
        await using var lifetime = new TaskSchedulerMiddlewareLifetime();
        var outer = new IndependentCompletion(events) { Hold = Gate() };
        var inner = new GatedCompletion(events);
        var scheduler = new TaskFlowSchedulerAdapter(TaskScheduler.Default)
            .UseMiddleware(new Factory(outer), lifetime).UseMiddleware(new Factory(inner));
        var operation = scheduler.Enqueue(() => 42);
        try
        {
            await inner.Entered.Task;
            await outer.Invoked.Task;
            inner.Release.SetResult(true);
            await Task.WhenAll(outer.Children);
            var shutdown = lifetime.DisposeAsync().AsTask();
            Assert.That(inner.Finished, Is.EqualTo(1));
            Assert.That(operation.IsCompleted, Is.False);
            Assert.That(shutdown.IsCompleted, Is.False);
            Assert.That(events, Does.Not.Contain("cleanup:inner"));
            outer.Hold.SetResult(true);
            Assert.That(await operation, Is.EqualTo(17));
            await shutdown;
            Assert.That(events.ToArray(), Is.EqualTo(FinishAndCleanupOrder));
        }
        finally { inner.Release.TrySetResult(true); outer.Hold.TrySetResult(true); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task NoNext_QuiescesAndCannotResurrect(bool throwBeforeNext)
    {
        var events = new ConcurrentQueue<string>();
        await using var lifetime = new TaskSchedulerMiddlewareLifetime();
        var outer = new IndependentCompletion(events) { Calls = 0, ThrowAfterNext = throwBeforeNext };
        var inner = new GatedCompletion(events);
        inner.Release.SetResult(true);
        var scheduler = new TaskFlowSchedulerAdapter(TaskScheduler.Default)
            .UseMiddleware(new Factory(outer), lifetime).UseMiddleware(new Factory(inner));
        var operation = scheduler.Enqueue(() => 42);
        if (throwBeforeNext)
            await Assert.ThatAsync(async () => await operation, Throws.Exception.SameAs(outer.Failure));
        else
            Assert.That(await operation, Is.EqualTo(17));
        await lifetime.DisposeAsync();
        Assert.That(inner.Calls, Is.EqualTo(throwBeforeNext ? 1 : 0));
        Assert.That(events.ToArray()[^2..], Is.EqualTo(CleanupOrder));
        await Assert.ThatAsync(async () => await outer.LateNext!(), Throws.TypeOf<InvalidOperationException>());
        Assert.That(inner.Calls, Is.EqualTo(throwBeforeNext ? 1 : 0));
    }

    [Test]
    public async Task OutstandingAdmission_RemainsReservedUntilChildQuiesces()
    {
        var events = new ConcurrentQueue<string>();
        await using var flow = new TaskFlow();
        var controller = new BoundedAdmissionController(new BoundedAdmissionOptions
        { Capacity = 1, CapacityMode = TaskSchedulerCapacityMode.Outstanding });
        var outer = new IndependentCompletion(events);
        var inner = new GatedCompletion(events);
        var bounded = flow.WithBoundedAdmission(controller);
        var scheduler = bounded
            .UseMiddleware(new Factory(outer)).UseMiddleware(new Factory(inner));
        var first = controller.Submit(scheduler, () => 42, TaskSchedulerAdmissionMode.Wait);
        try
        {
            await inner.Entered.Task;
            Assert.That(await first.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
            Assert.That(await first.Completion, Is.EqualTo(17));
            var rejected = controller.Submit(bounded, () => 43, TaskSchedulerAdmissionMode.Reject);
            Assert.That(await rejected.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Full));
            await Assert.ThatAsync(async () => await rejected.Completion, Throws.TypeOf<AdmissionRejectedException>());
            var waiting = controller.Submit(scheduler, () => 44, TaskSchedulerAdmissionMode.Wait);
            Assert.That(waiting.Admission.IsCompleted, Is.False);
            Assert.That(waiting.Completion.IsCompleted, Is.False);
            inner.Release.SetResult(true);
            Assert.That(await waiting.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
            Assert.That(await waiting.Completion, Is.EqualTo(17));
            await flow.DisposeAsync();
            Assert.That(inner.Finished, Is.EqualTo(2));
            Assert.That(events.ToArray()[^2..], Is.EqualTo(CleanupOrder));
        }
        finally { inner.Release.TrySetResult(true); }
    }

    [Test]
    public async Task RetainedContinuation_CanStillStartWhileAnIndependentChildIsActive()
    {
        var events = new ConcurrentQueue<string>();
        await using var lifetime = new TaskSchedulerMiddlewareLifetime();
        var outer = new IndependentCompletion(events);
        var inner = new GatedCompletion(events);
        var scheduler = new TaskFlowSchedulerAdapter(TaskScheduler.Default)
            .UseMiddleware(new Factory(outer), lifetime).UseMiddleware(new Factory(inner));
        try
        {
            Assert.That(await scheduler.Enqueue(() => 42), Is.EqualTo(17));
            var late = outer.LateNext!();
            Assert.That(inner.Calls, Is.EqualTo(2));
            var shutdown = lifetime.DisposeAsync().AsTask();
            Assert.That(shutdown.IsCompleted, Is.False);
            Assert.That(events, Is.Empty);
            inner.Release.SetResult(true);
            await late;
            await Task.WhenAll(outer.Children);
            await shutdown;
            Assert.That(inner.Finished, Is.EqualTo(2));
            Assert.That(events.ToArray()[^2..], Is.EqualTo(CleanupOrder));
        }
        finally { inner.Release.TrySetResult(true); }
    }

    [Test]
    public async Task DisposalRacingChildCompletion_CleansUpOnceAfterChild()
    {
        for (var iteration = 0; iteration < 30; iteration++)
        {
            var events = new ConcurrentQueue<string>();
            await using var lifetime = new TaskSchedulerMiddlewareLifetime();
            var outer = new IndependentCompletion(events);
            var inner = new GatedCompletion(events);
            var scheduler = new TaskFlowSchedulerAdapter(TaskScheduler.Default)
                .UseMiddleware(new Factory(outer), lifetime).UseMiddleware(new Factory(inner));
            Assert.That(await scheduler.Enqueue(() => 42), Is.EqualTo(17));
            var race = Gate();
            var shutdown = Task.Run(async () => { await race.Task; await lifetime.DisposeAsync(); });
            var finish = Task.Run(async () => { await race.Task; inner.Release.SetResult(true); });
            race.SetResult(true);
            await Task.WhenAll(shutdown, finish);
            Assert.That(events.ToArray(), Is.EqualTo(FinishAndCleanupOrder));
        }
    }

    private sealed class FaultedTerminal : ITaskScheduler
    {
        public Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
            => Task.FromException<T>(new FormatException("submission"));
    }

    private sealed class Factory(ITaskSchedulerMiddleware middleware) : ITaskSchedulerMiddlewareFactory
    {
        public ITaskSchedulerMiddleware Create(TaskSchedulerMiddlewareLifetime lifetime) => middleware;
    }

    private sealed class IndependentCompletion(ConcurrentQueue<string> events) : ITaskSchedulerCompletionMiddleware, IAsyncDisposable
    {
        public TaskCompletionSource<bool> Invoked { get; } = Gate();
        public int Calls { get; init; } = 1;
        public bool ThrowAfterNext { get; init; }
        public Exception Failure { get; } = new InvalidOperationException("outer");
        public CancellationToken? ReplacementToken { get; set; }
        public TaskCompletionSource<bool>? Hold { get; init; }
        public List<Task> Children { get; } = new();
        public List<Exception?> ChildFailures { get; } = new();
        public TaskSchedulerOperationContext? Context { get; private set; }
        public Func<Task>? LateNext { get; private set; }

        public async ValueTask<TaskSchedulerOperationOutcome<T>> InvokeAsync<T>(TaskSchedulerOperationContext context,
            TaskSchedulerOperationOutcome<T> outcome, TaskSchedulerCompletionDelegate<T> next)
        {
            Context = context;
            LateNext = () => next(context, outcome).AsTask();
            for (var i = 0; i < Calls; i++)
            {
                var child = next(ReplacementToken is { } token ? context.WithCancellationToken(token) : context, outcome);
                Children.Add(Observe(child));
            }
            Invoked.TrySetResult(true);
            if (ThrowAfterNext)
                throw Failure;
            if (Hold != null)
                await Hold.Task;
            return TaskSchedulerOperationOutcome<T>.FromResult((T)(object)17);
        }

        private async Task Observe<T>(ValueTask<TaskSchedulerOperationOutcome<T>> child)
        {
            var outcome = await child;
            lock (ChildFailures)
                ChildFailures.Add(outcome.Exception);
        }

        public ValueTask DisposeAsync() { events.Enqueue("cleanup:outer"); return default; }
    }

    private sealed class GatedCompletion(ConcurrentQueue<string> events) : ITaskSchedulerCompletionMiddleware, IAsyncDisposable
    {
        private int _finished;

        public TaskCompletionSource<bool> Entered { get; } = Gate();
        public TaskCompletionSource<bool> Release { get; } = Gate();
        public Exception? Failure { get; init; }
        public int Calls { get; private set; }
        public int Finished => Volatile.Read(ref _finished);
        public TaskSchedulerOperationContext? Context { get; private set; }

        public async ValueTask<TaskSchedulerOperationOutcome<T>> InvokeAsync<T>(TaskSchedulerOperationContext context,
            TaskSchedulerOperationOutcome<T> outcome, TaskSchedulerCompletionDelegate<T> next)
        {
            Calls++;
            Context = context;
            Entered.TrySetResult(true);
            await Release.Task;
            events.Enqueue("finish:inner");
            Interlocked.Increment(ref _finished);
            if (Failure != null)
                throw Failure;
            return await next(context, outcome);
        }

        public ValueTask DisposeAsync() { events.Enqueue("cleanup:inner"); return default; }
    }
}