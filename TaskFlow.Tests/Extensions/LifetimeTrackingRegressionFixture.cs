namespace TaskFlow.Tests.Extensions;

using NUnit.Framework;
using System.Threading.Tasks.Flow;
using System.Threading.Tasks.Flow.Internal;

[TestFixture]
[CancelAfter(10000)]
internal sealed class LifetimeTrackingRegressionFixture
{
    private static TaskCompletionSource<bool> Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Test]
    public async Task NestedOwnerCallbacks_RestoreAcrossAwait()
    {
        await using var first = new TaskSchedulerMiddlewareLifetime();
        await using var second = new TaskSchedulerMiddlewareLifetime();
        using (first.EnterOwnerCallback())
        {
            using (first.EnterOwnerCallback())
            using (second.EnterOwnerCallback())
            using (first.EnterOwnerCallback())
            {
                await Task.Yield();
                Assert.That(first.CheckReentrantDisposal, Throws.TypeOf<InvalidOperationException>());
                Assert.That(second.CheckReentrantDisposal, Throws.TypeOf<InvalidOperationException>());
            }
            await Task.Yield();
            Assert.That(first.CheckReentrantDisposal, Throws.TypeOf<InvalidOperationException>());
            Assert.That(second.CheckReentrantDisposal, Throws.Nothing);
        }
        Assert.That(first.CheckReentrantDisposal, Throws.Nothing);
    }

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

    [Test]
    public async Task AnnotationOnlySnapshot_DrainsAcceptedWorkAndRejectsClosedEntry()
    {
        var flow = new TaskFlow();
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
            await Assert.ThatAsync(async () => await scheduler.Enqueue(() => 0), Throws.TypeOf<ObjectDisposedException>());
            release.SetResult(true);
            Assert.That(await pending, Is.EqualTo(42));
            await shutdown;
        }
        finally { release.TrySetResult(true); await flow.DisposeAsync(); }
    }

    [Test]
    public async Task AnnotationOnlyCustomFlow_RetainsPipelineTracking()
    {
        await using var flow = new CustomFlow();
        var scheduler = flow.WithOperationName("custom");
        var entered = Gate();
        var release = Gate();
        var pending = scheduler.Enqueue<int>(async (_, _) =>
        {
            await Task.Yield();
            entered.SetResult(true);
            await release.Task;
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
        }
        finally { release.TrySetResult(true); }
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

    [TestCase(false)]
    [TestCase(true)]
    public async Task InheritedChildContext_CanDisposeExternallyAfterParentSettles(bool middleware)
    {
        await using var flow = new TaskFlow();
        ITaskScheduler scheduler = middleware ? flow.UseMiddleware(new Forwarding()) : flow;
        var release = Gate();
        var ambient = new AsyncLocal<string?> { Value = "caller" };
        Task? child = null;
        try
        {
            await scheduler.Enqueue((CancellationToken _) =>
            {
                child = Task.Run(async () =>
                {
                    await release.Task;
                    Assert.That(ambient.Value, Is.EqualTo("caller"));
                    await flow.DisposeAsync();
                }, CancellationToken.None);
            });
        }
        finally { release.TrySetResult(true); }
        await child!;
        await Assert.ThatAsync(async () => await scheduler.Enqueue(() => 42), Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public async Task NestedFactoriesAndCleanup_RejectReentrantDisposal()
    {
        var flow = new TaskFlow();
        var scheduler = flow.UseMiddleware(new Factory(lifetime =>
        {
            Assert.That(() => flow.DisposeAsync(), Throws.TypeOf<InvalidOperationException>());
            _ = flow.UseMiddleware(new Factory(nested =>
            {
                Assert.That(nested, Is.SameAs(lifetime));
                Assert.That(() => lifetime.DisposeAsync(), Throws.TypeOf<InvalidOperationException>());
                return new Forwarding();
            }));
            return new CleanupProbe(lifetime);
        }));
        Assert.That(await scheduler.Enqueue(() => 42), Is.EqualTo(42));
        await flow.DisposeAsync();
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

    [Test]
    public async Task DiscardedContinuation_HoldsCapacityAndLifetimeUntilUnderlyingWorkQuiesces()
    {
        var flow = new TaskFlow();
        var entered = Gate();
        var release = Gate();
        var abandoned = new Abandoning();
        var controller = new BoundedAdmissionController(new BoundedAdmissionOptions { Capacity = 1 });
        var scheduler = flow.UseMiddleware(abandoned).UseMiddleware(new Forwarding()).WithBoundedAdmission(controller);
        var first = scheduler.Enqueue<int>(async (_, _) =>
        {
            entered.SetResult(true);
            await release.Task;
            return 42;
        }, null, default);
        try
        {
            await entered.Task;
            Assert.That(await first, Is.EqualTo(17));
            var rejected = controller.Submit(scheduler, () => 2, TaskSchedulerAdmissionMode.Reject);
            Assert.That(await rejected.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Full));
            await Assert.ThatAsync(async () => await rejected.Completion, Throws.TypeOf<AdmissionRejectedException>());
            var shutdown = flow.DisposeAsync().AsTask();
            Assert.That(shutdown.IsCompleted, Is.False);
            release.SetResult(true);
            Assert.That(await abandoned.Underlying!, Is.EqualTo(42));
            await shutdown;
        }
        finally { release.TrySetResult(true); await flow.DisposeAsync(); }
    }

    [Test]
    public async Task InlineAndSynchronousFailureTerminals_PreserveResultsAndCancellation()
    {
        await using var lifetime = new TaskSchedulerMiddlewareLifetime();
        foreach (var failure in new Exception?[] { null, new FormatException("failure"), new OperationCanceledException(new CancellationToken(true)) })
        {
            var scheduler = new InlineTerminal(failure).UseMiddleware(new Factory(_ => new Forwarding()), lifetime)
                .UseMiddleware(new Forwarding()).UseMiddleware(new Forwarding());
            var task = scheduler.Enqueue(() => 42);
            if (failure == null)
                Assert.That(await task, Is.EqualTo(42));
            else
            {
                await Assert.ThatAsync(async () => await task, Throws.Exception.SameAs(failure));
                Assert.That(task.IsCanceled, Is.EqualTo(failure is OperationCanceledException));
            }
        }
    }

    private sealed class Forwarding : ITaskSchedulerEnqueueMiddleware
    {
        public Task<T> InvokeAsync<T>(TaskSchedulerEnqueueContext<T> context, TaskSchedulerEnqueueDelegate<T> next) => next(context);
    }

    private sealed class Abandoning : ITaskSchedulerEnqueueMiddleware
    {
        public Task<int>? Underlying { get; private set; }
        public Task<T> InvokeAsync<T>(TaskSchedulerEnqueueContext<T> context, TaskSchedulerEnqueueDelegate<T> next)
        {
            Underlying = (Task<int>)(object)next(context);
            return Task.FromResult((T)(object)17);
        }
    }

    private sealed class Factory(Func<TaskSchedulerMiddlewareLifetime, ITaskSchedulerMiddleware> create) : ITaskSchedulerMiddlewareFactory
    {
        public ITaskSchedulerMiddleware Create(TaskSchedulerMiddlewareLifetime lifetime) => create(lifetime);
    }

    private sealed class CleanupProbe(TaskSchedulerMiddlewareLifetime lifetime) : ITaskSchedulerEnqueueMiddleware, IAsyncDisposable
    {
        public Task<T> InvokeAsync<T>(TaskSchedulerEnqueueContext<T> context, TaskSchedulerEnqueueDelegate<T> next) => next(context);
        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            Assert.That(() => lifetime.DisposeAsync(), Throws.TypeOf<InvalidOperationException>());
        }
    }

    private sealed class CustomFlow : TaskFlowBase
    {
        public CustomFlow() : base(TaskFlowOptions.Default) => Ready();
        public override Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
            => taskFunc(state, cancellationToken).AsTask();
        protected override Task GetInitializationTask() => Task.CompletedTask;
        protected override Task GetCompletionTask() => Task.CompletedTask;
    }

    private sealed class InlineTerminal(Exception? failure) : ITaskScheduler
    {
        public Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
        {
            if (failure != null)
                throw failure;
            return taskFunc(state, cancellationToken).AsTask();
        }
    }
}