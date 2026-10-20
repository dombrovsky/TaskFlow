namespace TaskFlow.Tests.Extensions;

using NUnit.Framework;
using System.Threading.Tasks.Flow;

[TestFixture]
[CancelAfter(10000)]
internal sealed class LifetimeTrackingRegressionFixture
{
    private static TaskCompletionSource<bool> Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task BareAndAnnotatedFlows_ExternalDisposalDrainsSuppressedSubmission(bool dedicated, bool annotated)
    {
        await using TaskFlowBase flow = dedicated ? new DedicatedThreadTaskFlow() : new TaskFlow();
        ITaskScheduler scheduler = annotated ? flow.WithOperationName("external-owner") : flow;
        var ambient = new AsyncLocal<string?> { Value = "caller" };
        var entered = Gate();
        var release = Gate();
        Task<int> pending;
        using (ExecutionContext.SuppressFlow())
            pending = scheduler.Enqueue<int>(async (_, _) =>
            {
                Assert.That(ambient.Value, Is.Null);
                entered.SetResult(true);
                await release.Task;
                await Task.Yield();
                Assert.That(ambient.Value, Is.Null);
                return 42;
            }, null, default);
        try
        {
            await entered.Task;
            var shutdown = flow.DisposeAsync().AsTask();
            Assert.That(shutdown.IsCompleted, Is.False);
            release.SetResult(true);
            Assert.That(await pending, Is.EqualTo(42));
            await shutdown;
            Assert.That(ambient.Value, Is.EqualTo("caller"));
        }
        finally { release.TrySetResult(true); }
    }

    [Test]
    public async Task ExplicitLifetime_ExternalDisposalDrainsOpaqueTerminalAfterSuppressedFlow()
    {
        await using var lifetime = new TaskSchedulerMiddlewareLifetime();
        var scheduler = new TaskFlowSchedulerAdapter(TaskScheduler.Default)
            .UseMiddleware(new Factory(_ => new Forwarding()), lifetime);
        var entered = Gate();
        var release = Gate();
        Task<int> pending;
        using (ExecutionContext.SuppressFlow())
            pending = scheduler.Enqueue<int>(async (_, _) =>
            {
                await Task.Yield();
                entered.SetResult(true);
                await release.Task;
                return 42;
            }, null, default);
        try
        {
            await entered.Task;
            var shutdown = lifetime.DisposeAsync().AsTask();
            Assert.That(shutdown.IsCompleted, Is.False);
            release.SetResult(true);
            Assert.That(await pending, Is.EqualTo(42));
            await shutdown;
        }
        finally { release.TrySetResult(true); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AnnotationOnlySnapshot_DrainsAcceptedWorkAndRejectsClosedEntry(bool custom)
    {
        TaskFlowBase flow = custom ? new CustomFlow() : new TaskFlow();
        var scheduler = flow.WithOperationName("annotation-only");
        var entered = Gate();
        var release = Gate();
        var pending = scheduler.Enqueue<int>(async (_, _) =>
        {
            entered.SetResult(true);
            await release.Task;
            return 42;
        }, null, default);
        try
        {
            await entered.Task;
            var shutdown = flow.DisposeAsync().AsTask();
            Assert.That(shutdown.IsCompleted, Is.False);
            Task<int> rejected = null!;
            Assert.That(() => { rejected = scheduler.Enqueue(() => 0); }, Throws.TypeOf<ObjectDisposedException>());
            Assert.That(rejected, Is.Null, "Closed entry must reject the enqueue call before returning a task.");
            release.SetResult(true);
            Assert.That(await pending, Is.EqualTo(42));
            await shutdown;
        }
        finally { release.TrySetResult(true); await flow.DisposeAsync(); }
    }

    [Test]
    public async Task NestedFlows_ExternalDisposalDrainsBothOwnersAfterAwait()
    {
        await using var outer = new TaskFlow();
        await using var inner = new TaskFlow();
        var entered = Gate();
        var release = Gate();
        var pending = outer.Enqueue(async (CancellationToken _) =>
        {
            await Task.Yield();
            await inner.Enqueue(async (CancellationToken _) =>
            {
                await Task.Yield();
                entered.SetResult(true);
                await release.Task;
            }, CancellationToken.None);
        });
        try
        {
            await entered.Task;
            var outerShutdown = outer.DisposeAsync().AsTask();
            var innerShutdown = inner.DisposeAsync().AsTask();
            Assert.That(outerShutdown.IsCompleted, Is.False);
            Assert.That(innerShutdown.IsCompleted, Is.False);
            release.SetResult(true);
            await pending;
            await Task.WhenAll(outerShutdown, innerShutdown);
        }
        finally { release.TrySetResult(true); }
    }

    [Test]
    public async Task ConcurrentSubmissions_KeepTheirExecutionContextsAcrossAwait()
    {
        await using var flow = new TaskFlow();
        var scheduler = flow.UseMiddleware(new Forwarding()).UseMiddleware(new Forwarding());
        var ambient = new AsyncLocal<int>();
        var tasks = Enumerable.Range(1, 64).Select(index => Task.Run(async () =>
        {
            ambient.Value = index;
            return await scheduler.Enqueue<int>(async (_, _) =>
            {
                await Task.Yield();
                Assert.That(ambient.Value, Is.EqualTo(index));
                return index;
            }, null, default);
        })).ToArray();
        Assert.That(await Task.WhenAll(tasks), Is.EquivalentTo(Enumerable.Range(1, 64)));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task IndependentEnqueueTask_HoldsCapacityAndCleanupUntilBothBranchesFinish(bool childFirst, bool concurrentNext)
    {
        var flow = new TaskFlow();
        var entered = Gate();
        var release = Gate();
        IndependentEnqueue middleware = null!;
        var controller = new BoundedAdmissionController(new BoundedAdmissionOptions { Capacity = 1 });
        var scheduler = flow.UseMiddleware(new Factory(_ => middleware = new IndependentEnqueue(concurrentNext))).WithBoundedAdmission(controller);
        var pending = scheduler.Enqueue<int>(async (_, _) =>
        {
            entered.SetResult(true);
            await release.Task;
            return 42;
        }, null, default);
        try
        {
            await middleware.Started.Task;
            await entered.Task;
            if (childFirst)
            {
                release.SetResult(true);
                await middleware.Underlying!;
                Assert.That(pending.IsCompleted, Is.False);
            }
            else
            {
                middleware.Complete.SetResult(true);
                Assert.That(await pending, Is.Zero);
            }
            var rejected = controller.Submit(scheduler, () => 2, TaskSchedulerAdmissionMode.Reject);
            Assert.That(await rejected.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Full));
            await Assert.ThatAsync(async () => await rejected.Completion, Throws.TypeOf<AdmissionRejectedException>());
            var shutdown = flow.DisposeAsync().AsTask();
            Assert.That(shutdown.IsCompleted, Is.False);
            Assert.That(middleware.Cleaned, Is.False);
            release.TrySetResult(true);
            middleware.Complete.TrySetResult(true);
            Assert.That(await pending, Is.Zero);
            await middleware.Underlying!;
            await shutdown;
            Assert.That(middleware.Cleaned, Is.True);
        }
        finally
        {
            release.TrySetResult(true);
            middleware.Complete.TrySetResult(true);
            await flow.DisposeAsync();
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task InlineTerminals_PreserveContinuationResultsAndCancellation(bool faultedTask)
    {
        await using var lifetime = new TaskSchedulerMiddlewareLifetime();
        foreach (var failure in new Exception?[] { null, new FormatException("failure"), new OperationCanceledException(new CancellationToken(true)), new OperationCanceledException() })
        {
            var observer = new ObserveContinuation();
            var scheduler = new InlineTerminal(failure, faultedTask).UseMiddleware(new Factory(_ => new Forwarding()), lifetime)
                .UseMiddleware(new Forwarding()).UseMiddleware(observer);
            var task = scheduler.Enqueue(() => 42);
            if (failure == null)
                Assert.That(await task, Is.EqualTo(42));
            else
            {
                await Assert.ThatAsync(async () => await task, Throws.Exception.SameAs(failure));
                Assert.That(task.IsCanceled, Is.EqualTo(failure is OperationCanceledException));
                Assert.That(observer.Status, Is.EqualTo(failure is OperationCanceledException ? TaskStatus.Canceled : TaskStatus.Faulted));
            }
        }
    }

    private sealed class Forwarding : ITaskSchedulerEnqueueMiddleware
    {
        public Task<T> InvokeAsync<T>(TaskSchedulerEnqueueContext<T> context, TaskSchedulerEnqueueDelegate<T> next) => next(context);
    }

    private sealed class IndependentEnqueue(bool concurrentNext) : ITaskSchedulerEnqueueMiddleware, IDisposable
    {
        public TaskCompletionSource<bool> Complete { get; } = Gate();
        public TaskCompletionSource<bool> Started { get; } = Gate();
        public Task? Underlying { get; private set; }
        public bool Cleaned { get; private set; }
        public Task<T> InvokeAsync<T>(TaskSchedulerEnqueueContext<T> context, TaskSchedulerEnqueueDelegate<T> next)
        {
            Underlying = concurrentNext ? Task.Run(() => next(context)) : next(context);
            Started.SetResult(true);
            return Finish<T>();
        }
        private async Task<T> Finish<T>() { await Complete.Task; return default!; }
        public void Dispose() => Cleaned = true;
    }

    private sealed class ObserveContinuation : ITaskSchedulerEnqueueMiddleware
    {
        public TaskStatus Status { get; private set; }
        public async Task<T> InvokeAsync<T>(TaskSchedulerEnqueueContext<T> context, TaskSchedulerEnqueueDelegate<T> next)
        {
            var task = next(context);
            try
            { return await task; }
            finally { Status = task.Status; }
        }
    }

    private sealed class Factory(Func<TaskSchedulerMiddlewareLifetime, ITaskSchedulerMiddleware> create) : ITaskSchedulerMiddlewareFactory
    {
        public ITaskSchedulerMiddleware Create(TaskSchedulerMiddlewareLifetime lifetime) => create(lifetime);
    }

    private sealed class CustomFlow : TaskFlowBase
    {
        public CustomFlow() : base(TaskFlowOptions.Default) => Ready();
        public override Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
            => taskFunc(state, cancellationToken).AsTask();
        protected override Task GetInitializationTask() => Task.CompletedTask;
        protected override Task GetCompletionTask() => Task.CompletedTask;
    }

    private sealed class InlineTerminal(Exception? failure, bool faultedTask) : ITaskScheduler
    {
        public Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
        {
            if (failure != null)
            {
                if (faultedTask)
                    return Task.FromException<T>(failure);
                throw failure;
            }
            return taskFunc(state, cancellationToken).AsTask();
        }
    }
}