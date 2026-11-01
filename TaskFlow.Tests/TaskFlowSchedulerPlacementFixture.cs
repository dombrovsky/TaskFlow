namespace TaskFlow.Tests;

using NUnit.Framework;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks.Flow;

[TestFixture]
internal sealed class TaskFlowSchedulerPlacementFixture
{
    private static readonly int[] ExpectedOrder = [1, 2, 3];
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private static TaskCompletionSource<bool> Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [TestCase(false)]
    [TestCase(true)]
    public async Task PendingPredecessor_CallbackEntersConfiguredScheduler(bool installContext)
    {
        await using var scheduler = new SingleThreadScheduler(installContext);
        var flow = new TaskFlow(new TaskFlowOptions { TaskScheduler = scheduler });
        var release = Gate();
        var first = flow.Enqueue<int>((_, _) => new ValueTask<int>(CompleteFirst()), null, CancellationToken.None);
        async Task<int> CompleteFirst()
        { await release.Task.ConfigureAwait(false); return 1; }
        try
        {
            var second = flow.Enqueue<int>((_, _) =>
            {
                Assert.That(TaskScheduler.Current, Is.SameAs(scheduler), "callback-entry scheduler");
                return new ValueTask<int>(2);
            }, null, CancellationToken.None);

            // On the original implementation this marker runs after both startup wrappers,
            // proving the second awaited the still-gated predecessor. It also proves the
            // scheduler remains available while the predecessor is pending.
            await scheduler.DrainAsync();
            Assert.That(first.IsCompleted, Is.False);
            Assert.That(second.IsCompleted, Is.False);
            release.SetResult(true);
            Assert.That(await second.WaitAsync(TestTimeout), Is.EqualTo(2));
            Assert.That(await first.WaitAsync(TestTimeout), Is.EqualTo(1));
        }
        finally
        {
            release.TrySetResult(true);
            await flow.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        }
    }

    [Test]
    public async Task PredecessorOutcome_PreservesPlacementOrderAndOperationResult(
        [Values] bool pending, [Values("success", "fault", "cancel")] string outcome)
    {
        await using var scheduler = new SingleThreadScheduler();
        var flow = new TaskFlow(new TaskFlowOptions { TaskScheduler = scheduler });
        var release = Gate();
        var order = new List<int>();
        var active = 0;
        var failure = new InvalidOperationException("operation failed");
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        var first = flow.Enqueue<int>(async (_, _) =>
        {
            Assert.That(TaskScheduler.Current, Is.SameAs(scheduler));
            Assert.That(Interlocked.Increment(ref active), Is.EqualTo(1));
            order.Add(1);
            try
            {
                if (pending)
                    await release.Task.ConfigureAwait(false);
                if (outcome == "fault")
                    throw failure;
                if (outcome == "cancel")
                    throw new OperationCanceledException(canceled.Token);
                return 1;
            }
            finally { order.Add(2); Interlocked.Decrement(ref active); }
        }, null, CancellationToken.None);
        try
        {
            if (!pending)
            {
                // Awaiting the operation alone need not await the flow's no-op predecessor task.
                try
                { await first.WaitAsync(TestTimeout); }
                catch (Exception ex) when (ex == failure || ex is OperationCanceledException) { }
                await scheduler.DrainAsync();
                await scheduler.DrainAsync(); // Also drain the no-op continuation queued by the first wrapper.
            }
            var second = flow.Enqueue<int>((_, _) =>
            {
                Assert.That(TaskScheduler.Current, Is.SameAs(scheduler));
                Assert.That(Interlocked.Increment(ref active), Is.EqualTo(1));
                order.Add(3);
                Interlocked.Decrement(ref active);
                return new ValueTask<int>(2);
            }, null, CancellationToken.None);
            if (pending)
            {
                await scheduler.DrainAsync();
                Assert.That(first.IsCompleted, Is.False);
                Assert.That(second.IsCompleted, Is.False);
            }
            release.TrySetResult(true);
            Assert.That(await second.WaitAsync(TestTimeout), Is.EqualTo(2));
            Assert.That(order, Is.EqualTo(ExpectedOrder));
            if (outcome == "fault")
                await Assert.ThatAsync(async () => await first.WaitAsync(TestTimeout), Throws.TypeOf<InvalidOperationException>().And.SameAs(failure));
            else if (outcome == "cancel")
            {
                await Assert.ThatAsync(async () => await first.WaitAsync(TestTimeout),
                    Throws.InstanceOf<OperationCanceledException>().With.Property(nameof(OperationCanceledException.CancellationToken)).EqualTo(canceled.Token));
                Assert.That(first.IsCanceled, Is.True);
            }
            else
                Assert.That(await first.WaitAsync(TestTimeout), Is.EqualTo(1));
        }
        finally
        {
            release.TrySetResult(true);
            await flow.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        }
    }

    [Test]
    public async Task UserAwait_RespectsContextPrecedenceAndExplicitEscape([Values] bool installContext, [Values] bool capture)
    {
        await using var scheduler = new SingleThreadScheduler(installContext);
        var flow = new TaskFlow(new TaskFlowOptions { TaskScheduler = scheduler });
        var predecessor = Gate();
        var userRelease = Gate();
        var entered = Gate();
        var first = flow.Enqueue<int>(async (_, _) => { await predecessor.Task.ConfigureAwait(false); return 1; }, null, default);
        try
        {
            var second = flow.Enqueue<int>(async (_, _) =>
            {
                Assert.That(TaskScheduler.Current, Is.SameAs(scheduler));
                Assert.That(SynchronizationContext.Current, Is.SameAs(scheduler.Context));
                var threadId = Environment.CurrentManagedThreadId;
                entered.SetResult(true);
                await userRelease.Task.ConfigureAwait(capture);
                // A non-default SynchronizationContext wins over TaskScheduler for a user's capturing await.
                Assert.That(TaskScheduler.Current, Is.SameAs(capture && !installContext ? scheduler : TaskScheduler.Default));
                Assert.That(SynchronizationContext.Current, Is.SameAs(capture ? scheduler.Context : null));
                if (capture)
                    Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(threadId));
                return 2;
            }, null, default);
            var third = flow.Enqueue<int>((_, _) =>
            {
                Assert.That(TaskScheduler.Current, Is.SameAs(scheduler));
                return new ValueTask<int>(3);
            }, null, default);
            await scheduler.DrainAsync();
            Assert.That(second.IsCompleted, Is.False);
            predecessor.SetResult(true);
            await entered.Task.WaitAsync(TestTimeout);
            await scheduler.DrainAsync(); // User await has registered while its gate is still closed.
            Assert.That(third.IsCompleted, Is.False, "Logical operations may not overlap across user awaits");
            userRelease.SetResult(true);
            Assert.That(await second.WaitAsync(TestTimeout), Is.EqualTo(2));
            Assert.That(await third.WaitAsync(TestTimeout), Is.EqualTo(3));
            await first.WaitAsync(TestTimeout);
        }
        finally
        {
            predecessor.TrySetResult(true);
            userRelease.TrySetResult(true);
            await flow.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        }
    }

    [Test]
    public async Task PendingOperation_KeepsItsOwnExecutionContext([Values] bool suppress, [Values] bool installContext)
    {
        var ambient = new AsyncLocal<string?> { Value = "caller-first" };
        await using var scheduler = new SingleThreadScheduler(installContext);
        var flow = new TaskFlow(new TaskFlowOptions { TaskScheduler = scheduler });
        var release = Gate();
        var first = flow.Enqueue<int>(async (_, _) =>
        {
            Assert.That(ambient.Value, Is.EqualTo("caller-first"));
            ambient.Value = "predecessor-private";
            await release.Task.ConfigureAwait(false);
            Assert.That(ambient.Value, Is.EqualTo("predecessor-private"));
            return 1;
        }, null, default);
        try
        {
            await scheduler.DrainAsync();
            ambient.Value = "caller-second";
            var state = new object();
            async ValueTask<int> Callback(object? suppliedState, CancellationToken token)
            {
                Assert.That(TaskScheduler.Current, Is.SameAs(scheduler));
                Assert.That(suppliedState, Is.SameAs(state));
                Assert.That(ambient.Value, Is.EqualTo(suppress ? null : "caller-second"));
                ambient.Value = "second-private";
                await Task.Yield();
                Assert.That(ambient.Value, Is.EqualTo("second-private"));
                return 2;
            }
            Task<int> second;
            if (suppress)
            {
                using (ExecutionContext.SuppressFlow())
                    second = flow.Enqueue(Callback, state, default);
            }
            else
                second = flow.Enqueue(Callback, state, default);
            await scheduler.DrainAsync();
            Assert.That(second.IsCompleted, Is.False);
            release.SetResult(true);
            Assert.That(await second.WaitAsync(TestTimeout), Is.EqualTo(2));
            await first.WaitAsync(TestTimeout);
            Assert.That(ambient.Value, Is.EqualTo("caller-second"));
            Assert.That(await flow.Enqueue(() => ambient.Value).WaitAsync(TestTimeout), Is.EqualTo("caller-second"));
        }
        finally
        {
            release.TrySetResult(true);
            await flow.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        }
    }

    [TestCase("caller")]
    [TestCase("async-dispose")]
    [TestCase("sync-dispose")]
    [SuppressMessage("Usage", "CA1849:Call async methods when in an async method", Justification = "Tests the zero-timeout synchronous disposal API during a pending predecessor.")]
    public async Task CancellationDuringPredecessor_DrainsAcceptedCallbacksOnScheduler(string kind)
    {
        await using var scheduler = new SingleThreadScheduler();
        var flow = new TaskFlow(new TaskFlowOptions { TaskScheduler = scheduler });
        using var caller = new CancellationTokenSource();
        var release = Gate();
        var first = flow.Enqueue<int>(async (_, _) => { await release.Task.ConfigureAwait(false); return 1; }, null, default);
        try
        {
            var second = flow.Enqueue<int>((_, token) =>
            {
                Assert.That(TaskScheduler.Current, Is.SameAs(scheduler));
                Assert.That(token.IsCancellationRequested, Is.True);
                token.ThrowIfCancellationRequested();
                return new ValueTask<int>(2);
            }, null, caller.Token);
            var third = flow.Enqueue<int>((_, token) =>
            {
                Assert.That(TaskScheduler.Current, Is.SameAs(scheduler));
                Assert.That(token.IsCancellationRequested, Is.EqualTo(kind != "caller"));
                return new ValueTask<int>(3); // Cooperative cancellation may also be ignored.
            }, null, default);
            await scheduler.DrainAsync();
            Task? shutdown = null;
            if (kind == "caller")
                await caller.CancelAsync();
            else
            {
                if (kind == "sync-dispose")
                    Assert.That(flow.Dispose(TimeSpan.Zero), Is.False);
                shutdown = flow.DisposeAsync().AsTask();
                Assert.That(shutdown.IsCompleted, Is.False);
                Assert.That(() => flow.Enqueue(() => 0), Throws.TypeOf<ObjectDisposedException>());
            }
            Assert.That(first.IsCompleted, Is.False);
            Assert.That(second.IsCompleted, Is.False);
            release.SetResult(true);
            await Assert.ThatAsync(async () => await second.WaitAsync(TestTimeout), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(second.IsCanceled, Is.True);
            Assert.That(await third.WaitAsync(TestTimeout), Is.EqualTo(3));
            Assert.That(await first.WaitAsync(TestTimeout), Is.EqualTo(1));
            if (shutdown is not null)
                await shutdown.WaitAsync(TestTimeout);
        }
        finally
        {
            release.TrySetResult(true);
            await flow.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SchedulerRejectsInitialOperation_PreservesSynchronousFailure(bool disposed)
    {
        await using var scheduler = new SingleThreadScheduler();
        await using var flow = new TaskFlow(new TaskFlowOptions { TaskScheduler = scheduler });
        Exception failure = disposed ? new ObjectDisposedException("scheduler") : new InvalidOperationException("rejected");
        scheduler.Rejection = failure;
        Assert.That(() => flow.Enqueue(() => 42),
            Throws.TypeOf<TaskSchedulerException>().With.InnerException.SameAs(failure));
        scheduler.Rejection = null;
        Assert.That(await flow.Enqueue(() => 43).WaitAsync(TestTimeout), Is.EqualTo(43));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SchedulerRejectsAfterPredecessor_FaultsPendingTaskAndAllowsShutdown(bool disposeScheduler)
    {
        await using var scheduler = new SingleThreadScheduler();
        var flow = new TaskFlow(new TaskFlowOptions { TaskScheduler = scheduler });
        var release = Gate();
        var first = flow.Enqueue<int>(async (_, _) => { await release.Task.ConfigureAwait(false); return 1; }, null, default);
        var invoked = false;
        try
        {
            var second = flow.Enqueue(() => { invoked = true; return 2; });
            await scheduler.DrainAsync();
            Assert.That(second.IsCompleted, Is.False);
            var failure = new InvalidOperationException("rejected after predecessor wait");
            if (disposeScheduler)
                await scheduler.DisposeAsync();
            else
                scheduler.Rejection = failure;
            release.SetResult(true);
            var exception = Assert.ThrowsAsync<TaskSchedulerException>(async () => await second.WaitAsync(TestTimeout));
            if (disposeScheduler)
                Assert.That(exception!.InnerException, Is.TypeOf<ObjectDisposedException>());
            else
                Assert.That(exception!.InnerException, Is.SameAs(failure));
            Assert.That(invoked, Is.False);
            Assert.That(await first.WaitAsync(TestTimeout), Is.EqualTo(1));
            if (!disposeScheduler)
            {
                scheduler.Rejection = null;
                Assert.That(await flow.Enqueue(() => 3).WaitAsync(TestTimeout), Is.EqualTo(3));
            }
        }
        finally
        {
            release.TrySetResult(true);
            await flow.DisposeAsync().AsTask().WaitAsync(TestTimeout);
        }
    }

    [Test]
    public async Task EnqueueFromScheduler_DoesNotInlineOrReenterCallback()
    {
        await using var scheduler = new SingleThreadScheduler();
        await using var flow = new TaskFlow(new TaskFlowOptions { TaskScheduler = scheduler });
        var operation = Task.Factory.StartNew(() =>
        {
            var invoked = false;
            var result = flow.Enqueue(() => { invoked = true; return 42; });
            Assert.That(invoked, Is.False);
            return result;
        }, CancellationToken.None, TaskCreationOptions.None, scheduler).Unwrap();
        Assert.That(await operation.WaitAsync(TestTimeout), Is.EqualTo(42));
    }

    [Test]
    public async Task Adapter_PreservesSchedulerEntryWithoutSerializingLogicalOperations()
    {
        await using var scheduler = new SingleThreadScheduler(true);
        var adapter = new TaskFlowSchedulerAdapter(scheduler);
        var release = Gate();
        var first = adapter.Enqueue<int>(async (_, _) => { await release.Task.ConfigureAwait(false); return 1; }, null, default);
        try
        {
            var second = adapter.Enqueue(() =>
            {
                Assert.That(TaskScheduler.Current, Is.SameAs(scheduler));
                Assert.That(SynchronizationContext.Current, Is.SameAs(scheduler.Context));
                Assert.That(first.IsCompleted, Is.False);
                return 2;
            });
            Assert.That(await second.WaitAsync(TestTimeout), Is.EqualTo(2));
        }
        finally { release.TrySetResult(true); await first.WaitAsync(TestTimeout); }
    }

    private sealed class SingleThreadScheduler : TaskScheduler, IAsyncDisposable
    {
        private readonly BlockingCollection<Action> _queue = new();
        private readonly TaskCompletionSource<bool> _exited = Gate();
        private readonly SynchronizationContext? _context;

        private int _disposed;

        public SingleThreadScheduler(bool installContext = false)
        {
            _context = installContext ? new PostingContext(_queue) : null;
            var thread = new Thread(Run) { IsBackground = true, Name = "TaskFlow scheduler regression" };
            // A worker's ambient values must not inherit the test's ExecutionContext.
            using (ExecutionContext.SuppressFlow())
                thread.Start();
        }

        public SynchronizationContext? Context => _context;
        public Exception? Rejection { get; set; }

        public Task DrainAsync() => Task.Factory.StartNew(static () => { }, CancellationToken.None,
            TaskCreationOptions.None, this).WaitAsync(TestTimeout);

        protected override IEnumerable<Task> GetScheduledTasks() => [];
        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;
        protected override void QueueTask(Task task)
        {
            if (Rejection is { } failure)
                throw failure;
            _queue.Add(() => TryExecuteTask(task));
        }

        private void Run()
        {
            try
            {
                foreach (var action in _queue.GetConsumingEnumerable())
                {
                    SynchronizationContext.SetSynchronizationContext(_context);
                    action();
                }
            }
            finally { _exited.TrySetResult(true); }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            _queue.CompleteAdding();
            await _exited.Task.WaitAsync(TestTimeout);
            _queue.Dispose();
        }

        // Dispatch raw callbacks on the same physical thread, without a Task carrying scheduler identity.
        private sealed class PostingContext(BlockingCollection<Action> queue) : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object? state) => queue.Add(() => d(state));
        }
    }
}