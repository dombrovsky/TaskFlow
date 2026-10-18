namespace TaskFlow.Tests.Extensions;

using NUnit.Framework;
using System.Threading.Tasks.Flow;
using System.Threading.Tasks.Flow.Internal;

[CancelAfter(30000)]
internal sealed class DispatchCoalescingFixture
{
    [ThreadStatic]
    private static bool _submitting;

    private static AdmissionTestGate Gate() => new();
    private static BoundedAdmissionController Budget(int capacity) => new(new BoundedAdmissionOptions { Capacity = capacity });
    private static PipelineTaskScheduler Configure(int kind, TaskFlow flow, BoundedAdmissionController budget, int concurrency)
    {
        ITaskScheduler root = new TaskFlowSchedulerAdapter(flow.Options.TaskScheduler);
        root = kind switch
        {
            1 => flow.WithKeyedScheduling<int>().WithConcurrencyLimit(concurrency),
            2 => flow.WithConcurrencyLimit(concurrency),
            _ => root
        };
        return (PipelineTaskScheduler)root.WithBoundedAdmission(budget, flow.MiddlewareLifetime);
    }

    private static TaskSchedulerTrySubmission<T> Submit<T>(int kind, BoundedAdmissionController budget, ITaskScheduler scheduler, int key, Func<ValueTask<T>> operation, CancellationToken token = default)
    {
        _submitting = true;
        try
        {
            return kind == 1
                ? budget.TrySubmit(scheduler, key, (_, _) => operation(), token)
                : budget.TrySubmit(scheduler, _ => operation(), token);
        }
        finally { _submitting = false; }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task ConcurrentBurst_UsesOneDrain_AndNeverCallsUserCodeOnProducer(int kind)
    {
        using var backend = new InlineBackend(blockFirst: true);
        await using var flowCleanup = new AdmissionTestOwner<TaskFlow>(() => new TaskFlow(new TaskFlowOptions { TaskScheduler = backend }));
        var flow = flowCleanup.Value;
        var budget = Budget(513);
        var scheduler = Configure(kind, flow, budget, 513);
        var observer = new Observer();
        var decorated = scheduler.UseMiddleware(observer);
        var tasks = new Task<int>[513];
        tasks[0] = Submit(kind, budget, decorated, 0, () => new ValueTask<int>(0)).Completion!;
        try
        {
            await backend.Entered.Task.WaitForTestAsync();
            await Task.WhenAll(Enumerable.Range(0, 8).Select(producer => Task.Run(() =>
            {
                for (var i = producer + 1; i < tasks.Length; i += 8)
                {
                    var value = i;
                    var attempt = Submit(kind, budget, decorated, i, () =>
                    {
                        Assert.That(_submitting, Is.False);
                        return new ValueTask<int>(value);
                    });
                    Assert.That(attempt.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
                    tasks[i] = attempt.Completion!;
                }
            }))).WaitForTestAsync();
            Assert.That(scheduler.ScheduledDrainCount, Is.EqualTo(1));
            Assert.That(Submit(kind, budget, decorated, 900, () => new ValueTask<int>(0)).Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Full));
            Assert.That(scheduler.ScheduledDrainCount, Is.EqualTo(1));
        }
        finally { backend.Release.Set(); }
        Assert.That(await Task.WhenAll(tasks).WaitForTestAsync(), Is.EqualTo(Enumerable.Range(0, tasks.Length)));
        Assert.That(scheduler.ScheduledDrainCount, Is.EqualTo(1));
        Assert.That(backend.InlineOnProducer, Is.Zero);
        Assert.That(observer.InlineOnProducer, Is.Zero);
    }

    [TestCase(1)]
    [TestCase(2)]
    public async Task SaturatedExecution_DoesNotScheduleDrainsForBlockedArrivals(int kind)
    {
        using var backend = new InlineBackend();
        await using var flowCleanup = new AdmissionTestOwner<TaskFlow>(() => new TaskFlow(new TaskFlowOptions { TaskScheduler = backend }));
        var flow = flowCleanup.Value;
        var budget = Budget(257);
        var scheduler = Configure(kind, flow, budget, 1);
        var idle = Gate();
        var release = Gate();
        scheduler.DrainIdle = () => idle.TrySetResult(0);
        var first = Submit(kind, budget, scheduler, 0, () => new ValueTask<int>(release.Task));
        try
        {
            await idle.Task.WaitForTestAsync();
            var pending = Enumerable.Range(1, 256).Select(i => Submit(kind, budget, scheduler, i, () => new ValueTask<int>(i)).Completion!).ToArray();
            Assert.That(scheduler.ScheduledDrainCount, Is.EqualTo(1));
            release.SetResult(0);
            await first.Completion!.WaitForTestAsync();
            Assert.That(await Task.WhenAll(pending).WaitForTestAsync(), Is.EqualTo(Enumerable.Range(1, 256)));
        }
        finally { release.TrySetResult(0); }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task ArrivalAfterIdlePublication_StartsAnotherDrainWithoutLostWakeup(int kind)
    {
        using var backend = new InlineBackend();
        using var retire = new ManualResetEventSlim();
        await using var flowCleanup = new AdmissionTestOwner<TaskFlow>(() => new TaskFlow(new TaskFlowOptions { TaskScheduler = backend }));
        var flow = flowCleanup.Value;
        var budget = Budget(2);
        var scheduler = Configure(kind, flow, budget, 2);
        var idle = Gate();
        var retired = Gate();
        var calls = 0;
        scheduler.DrainIdle = () =>
        {
            if (Interlocked.Increment(ref calls) != 1)
                return;
            idle.SetResult(0);
            try
            { retire.WaitForTest(); retired.TrySetResult(0); }
            catch (OperationCanceledException error) { retired.TrySetException(error); }
            catch (TimeoutException error) { retired.TrySetException(error); }
        };
        try
        {
            await Submit(kind, budget, scheduler, 0, () => new ValueTask<int>(0)).Completion!.WaitForTestAsync();
            await idle.Task.WaitForTestAsync();
            Assert.That(await Submit(kind, budget, scheduler, 1, () => new ValueTask<int>(1)).Completion!.WaitForTestAsync(), Is.EqualTo(1));
            Assert.That(scheduler.ScheduledDrainCount, Is.EqualTo(2));
        }
        finally { retire.Set(); await retired.WaitForCleanupAsync(); }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task SharedDrain_RestoresEachContextIncludingSuppression_AndLifetimeOwnership(int kind)
    {
        using var backend = new InlineBackend(blockFirst: true);
        await using var flowCleanup = new AdmissionTestOwner<TaskFlow>(() => new TaskFlow(new TaskFlowOptions { TaskScheduler = backend }));
        var flow = flowCleanup.Value;
        var lifetime = flow.MiddlewareLifetime;
        var budget = Budget(4);
        var scheduler = Configure(kind, flow, budget, 4);
        var ambient = new AsyncLocal<string?>();
        backend.CheckOwnership = () => Assert.That(() => lifetime.DisposeAsync(), Throws.TypeOf<InvalidOperationException>());
        ValueTask<string?> Read()
        {
            Assert.That(_submitting, Is.False);
            Assert.That(() => lifetime.DisposeAsync(), Throws.TypeOf<InvalidOperationException>());
            return new ValueTask<string?>(ambient.Value);
        }
        ambient.Value = "first";
        var first = Submit(kind, budget, scheduler, 0, Read);
        TaskSchedulerTrySubmission<string?> second;
        TaskSchedulerTrySubmission<string?> suppressed;
        TaskSchedulerTrySubmission<string?> fourth;
        try
        {
            await backend.Entered.Task.WaitForTestAsync();
            ambient.Value = "second";
            second = Submit(kind, budget, scheduler, 1, Read);
            using (ExecutionContext.SuppressFlow())
                suppressed = Submit(kind, budget, scheduler, 2, Read);
            ambient.Value = "fourth";
            fourth = Submit(kind, budget, scheduler, 3, Read);
        }
        finally { backend.Release.Set(); }
        Assert.That(await Task.WhenAll(first.Completion!, second.Completion!, suppressed.Completion!, fourth.Completion!).WaitForTestAsync(), Is.EqualTo(new string?[] { "first", "second", null, "fourth" }));
        Assert.That(scheduler.ScheduledDrainCount, Is.EqualTo(1));
        Assert.That(backend.InlineOnProducer, Is.Zero);
    }

    [TestCase(0, false)]
    [TestCase(1, false)]
    [TestCase(2, false)]
    [TestCase(0, true)]
    [TestCase(1, true)]
    [TestCase(2, true)]
    public async Task ManyProducersAndShortOperations_DoNotStrandWork(int kind, bool inline)
    {
        using var backend = new InlineBackend();
        await using var flowCleanup = new AdmissionTestOwner<TaskFlow>(() => new TaskFlow(new TaskFlowOptions { TaskScheduler = inline ? backend : TaskScheduler.Default }));
        var flow = flowCleanup.Value;
        var budget = Budget(8000);
        var scheduler = Configure(kind, flow, budget, 4);
        var active = new int[16];
        var executed = 0;
        await Task.WhenAll(Enumerable.Range(0, 8).Select(producer => Task.Run(async () =>
        {
            var completions = new Task<int>[1000];
            for (var i = 0; i < completions.Length; i++)
            {
                var key = (producer + i) % active.Length;
                var attempt = Submit(kind, budget, scheduler, key, () =>
                {
                    Assert.That(_submitting, Is.False);
                    if (kind == 1)
                        Assert.That(Interlocked.Increment(ref active[key]), Is.EqualTo(1));
                    Interlocked.Increment(ref executed);
                    if (kind == 1)
                        Interlocked.Decrement(ref active[key]);
                    return new ValueTask<int>(1);
                });
                Assert.That(attempt.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
                completions[i] = attempt.Completion!;
            }
            await Task.WhenAll(completions).WaitForTestAsync();
        }))).WaitForTestAsync();
        Assert.That(executed, Is.EqualTo(8000));
        Assert.That(await Submit(kind, budget, scheduler, 0, () => new ValueTask<int>(42)).Completion!.WaitForTestAsync(), Is.EqualTo(42));
        Assert.That(backend.InlineOnProducer, Is.Zero);
    }

    [TestCase(0)]
    [TestCase(1)]
    public async Task LegacyInlineDrain_HandsOffBeforeSuppressedTrySubmit(int kind)
    {
        using var backend = new InlineBackend(blockFirst: true);
        await using var flowCleanup = new AdmissionTestOwner<TaskFlow>(() => new TaskFlow(new TaskFlowOptions { TaskScheduler = backend }));
        var flow = flowCleanup.Value;
        var budget = Budget(2);
        var scheduler = Configure(kind, flow, budget, 2);
        var ambient = new AsyncLocal<string?>();
        var legacy = Task.Run(() =>
        {
            ambient.Value = "legacy producer";
            return kind == 1 ? scheduler.Enqueue(0, _ => 1) : scheduler.Enqueue(() => 1);
        });
        TaskSchedulerTrySubmission<string?> attempt;
        try
        {
            await backend.Entered.Task.WaitForTestAsync();
            using (ExecutionContext.SuppressFlow())
                attempt = Submit(kind, budget, scheduler, 1, () => new ValueTask<string?>(ambient.Value));
            Assert.That(scheduler.ScheduledDrainCount, Is.Zero);
        }
        finally { backend.Release.Set(); }
        await legacy.WaitForTestAsync();
        Assert.That(await attempt.Completion!.WaitForTestAsync(), Is.Null);
        Assert.That(scheduler.ScheduledDrainCount, Is.EqualTo(1));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1849:Call async methods when in an async method", Justification = "Exercises synchronous cancellation callbacks racing dispatch and shutdown.")]
    public async Task CancellationCompletionAndShutdown_DoNotStrandPendingWork(int kind)
    {
        for (var iteration = 0; iteration < 12; iteration++)
        {
            using var backend = new InlineBackend();
            await using var flowCleanup = new AdmissionTestOwner<TaskFlow>(() => new TaskFlow(new TaskFlowOptions { TaskScheduler = backend }));
            var flow = flowCleanup.Value;
            using var cancellation = new CancellationTokenSource();
            var budget = Budget(129);
            var scheduler = Configure(kind, flow, budget, 1);
            var started = Gate();
            var release = Gate();
            var first = Submit(kind, budget, scheduler, 0, () => { started.SetResult(0); return new ValueTask<int>(release.Task); });
            var go = Gate();
            try
            {
                await started.Task.WaitForTestAsync();
                var pending = Enumerable.Range(1, 128).Select(_ => Submit(kind, budget, scheduler, 0, () => new ValueTask<int>(1), cancellation.Token).Completion!).ToArray();
                await Task.WhenAll(
                    Task.Run(async () => { await go.Task.WaitForTestAsync(); cancellation.Cancel(); }),
                    Task.Run(async () => { await go.Task.WaitForTestAsync(); release.TrySetResult(0); }),
                    Task.Run(async () => { go.SetResult(0); await flow.DisposeAsync().WaitForTestAsync(); })).WaitForTestAsync();
                await first.Completion!.WaitForTestAsync();
                foreach (var task in pending)
                {
                    try
                    { await task.WaitForTestAsync(); }
                    catch (OperationCanceledException) { }
                    catch (ObjectDisposedException) { }
                }
                Assert.That(Submit(kind, budget, scheduler, 0, () => new ValueTask<int>(1)).Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Closed));
                Assert.That(scheduler.Coordinator?.KeyCount ?? 0, Is.Zero);
            }
            finally { go.TrySetResult(0); release.TrySetResult(0); }
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1849:Call async methods when in an async method", Justification = "Exercises synchronous cancellation callbacks racing admission.")]
    public async Task CancellationRacingAdmission_DoesNotLeaveAStaleReadyHead(int kind)
    {
        using var backend = new InlineBackend();
        await using var flowCleanup = new AdmissionTestOwner<TaskFlow>(() => new TaskFlow(new TaskFlowOptions { TaskScheduler = backend }));
        var flow = flowCleanup.Value;
        var budget = Budget(1);
        var scheduler = Configure(kind, flow, budget, 1);
        for (var i = 0; i < 100; i++)
        {
            using var cancellation = new CancellationTokenSource();
            var go = Gate();
            var producer = Task.Run(async () =>
            {
                await go.Task.WaitForTestAsync();
                try
                {
                    var result = Submit(kind, budget, scheduler, 0, () => new ValueTask<int>(1), cancellation.Token);
                    Assert.That(result.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
                    await result.Completion!.WaitForTestAsync();
                }
                catch (OperationCanceledException) { }
            });
            var cancel = Task.Run(async () => { await go.Task.WaitForTestAsync(); cancellation.Cancel(); });
            go.SetResult(0);
            await Task.WhenAll(producer, cancel).WaitForTestAsync();
        }
        Assert.That(await Submit(kind, budget, scheduler, 0, () => new ValueTask<int>(42)).Completion!.WaitForTestAsync(), Is.EqualTo(42));
    }

    [TestCase(0)]
    [TestCase(1)]
    public async Task SuppressedRejectionCompletion_RetainsOwnershipAndRunsOffProducer(int kind)
    {
        using var backend = new InlineBackend();
        await using var flowCleanup = new AdmissionTestOwner<TaskFlow>(() => new TaskFlow(new TaskFlowOptions { TaskScheduler = backend }));
        var flow = flowCleanup.Value;
        var lifetime = flow.MiddlewareLifetime;
        var budget = Budget(1);
        var scheduler = Configure(kind, flow, budget, 1);
        var completion = new RejectionObserver(lifetime);
        var decorated = scheduler.UseMiddleware(completion);
        var release = Gate();
        var first = Submit(kind, budget, decorated, 0, () => new ValueTask<int>(release.Task));
        try
        {
            using (ExecutionContext.SuppressFlow())
                Assert.That(Submit(kind, budget, decorated, 1, () => new ValueTask<int>(1)).Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Full));
            await completion.Checked.Task.WaitForTestAsync();
        }
        finally { release.TrySetResult(0); }
        await first.Completion!.WaitForTestAsync();
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task SynchronousSchedulingAndCallbackFailures_DoNotAbandonTheDrain(int kind)
    {
        using var backend = new InlineBackend(blockFirst: true, rejectFirst: true);
        await using var flowCleanup = new AdmissionTestOwner<TaskFlow>(() => new TaskFlow(new TaskFlowOptions { TaskScheduler = backend }));
        var flow = flowCleanup.Value;
        var budget = Budget(64);
        var scheduler = Configure(kind, flow, budget, 64);
        ITaskScheduler decorated = scheduler.UseMiddleware(new FailingCompletion());
        if (kind == 1)
            decorated = decorated.WithPerKeyBoundedAdmission(new BoundedAdmissionOptions { Capacity = 1 });
        var firstRan = false;
        var first = Submit(kind, budget, decorated, 0, () => { firstRan = true; return new ValueTask<int>(0); });
        var pending = new Task<int>[63];
        try
        {
            await backend.Entered.Task.WaitForTestAsync();
            pending[0] = Submit<int>(kind, budget, decorated, 1, () => throw new FormatException("Delegate failure")).Completion!;
            pending[1] = Submit(kind, budget, decorated, 2, () => new ValueTask<int>(2)).Completion!;
            for (var index = 2; index < pending.Length; index++)
            {
                var value = index + 1;
                pending[index] = Submit(kind, budget, decorated, value, () => new ValueTask<int>(value)).Completion!;
            }
            Assert.That(scheduler.ScheduledDrainCount, Is.EqualTo(1));
        }
        finally { backend.Release.Set(); }
        await Assert.ThatAsync(async () => await first.Completion!.WaitForTestAsync(), Throws.TypeOf<TaskSchedulerException>());
        await Assert.ThatAsync(async () => await (pending[0]).WaitForTestAsync(), Throws.TypeOf<FormatException>());
        await Assert.ThatAsync(async () => await (pending[1]).WaitForTestAsync(), Throws.TypeOf<InvalidOperationException>());
        Assert.That(await Task.WhenAll(pending.Skip(2)).WaitForTestAsync(), Is.EqualTo(Enumerable.Range(3, 61)));
        Assert.That(firstRan, Is.False);
        Assert.That(scheduler.ScheduledDrainCount, Is.EqualTo(1));
        Assert.That(backend.InlineOnProducer, Is.Zero);
        // The failed key and all global reservations are reusable after observable completion.
        Assert.That(await Submit(kind, budget, decorated, 0, () => new ValueTask<int>(42)).Completion!.WaitForTestAsync(), Is.EqualTo(42));
        await flow.DisposeAsync().WaitForTestAsync();
    }

    private sealed class FailingCompletion : ITaskSchedulerCompletionMiddleware
    {
        public ValueTask<TaskSchedulerOperationOutcome<T>> InvokeAsync<T>(TaskSchedulerOperationContext context,
            TaskSchedulerOperationOutcome<T> outcome, TaskSchedulerCompletionDelegate<T> next)
        {
            if (outcome.IsSuccess && outcome.Result is int value && value == 2)
                throw new InvalidOperationException("Completion failure");
            return next(context, outcome);
        }
    }

    private sealed class Observer : ITaskSchedulerExecutionMiddleware, ITaskSchedulerCompletionMiddleware
    {
        private int _inlineOnProducer;
        public int InlineOnProducer => _inlineOnProducer;
        private void Check() { if (_submitting) Interlocked.Increment(ref _inlineOnProducer); }
        public ValueTask<T> InvokeAsync<T>(TaskSchedulerOperationContext context, TaskSchedulerExecutionDelegate<T> next)
        { Check(); return next(context); }
        public ValueTask<TaskSchedulerOperationOutcome<T>> InvokeAsync<T>(TaskSchedulerOperationContext context, TaskSchedulerOperationOutcome<T> outcome, TaskSchedulerCompletionDelegate<T> next)
        { Check(); return next(context, outcome); }
    }

    private sealed class RejectionObserver(TaskSchedulerMiddlewareLifetime lifetime) : ITaskSchedulerCompletionMiddleware
    {
        public AdmissionTestGate Checked { get; } = Gate();
        public ValueTask<TaskSchedulerOperationOutcome<T>> InvokeAsync<T>(TaskSchedulerOperationContext context, TaskSchedulerOperationOutcome<T> outcome, TaskSchedulerCompletionDelegate<T> next)
        {
            if (outcome.Exception is AdmissionRejectedException)
            {
                try
                {
                    Assert.That(_submitting, Is.False);
                    Assert.That(() => lifetime.DisposeAsync(), Throws.TypeOf<InvalidOperationException>());
                    Checked.TrySetResult(0);
                }
                catch (Exception error) { Checked.TrySetException(error); throw; }
            }
            return next(context, outcome);
        }
    }

    private sealed class InlineBackend(bool blockFirst = false, bool rejectFirst = false) : TaskScheduler, IDisposable
    {
        private int _queued;
        private int _inlineOnProducer;
        public ManualResetEventSlim Release { get; } = new();
        public AdmissionTestGate Entered { get; } = Gate();
        public Action? CheckOwnership { get; set; }
        public int InlineOnProducer => _inlineOnProducer;
        protected override IEnumerable<Task> GetScheduledTasks() => Array.Empty<Task>();
        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => TryExecuteTask(task);
        protected override void QueueTask(Task task)
        {
            if (_submitting)
                Interlocked.Increment(ref _inlineOnProducer);
            CheckOwnership?.Invoke();
            var first = Interlocked.Increment(ref _queued) == 1;
            if (first && blockFirst)
            {
                Entered.SetResult(0);
                Release.WaitForTest();
            }
            if (first && rejectFirst)
                throw new InvalidOperationException("Scheduler failure");
            TryExecuteTask(task);
        }
        public void Dispose() => Release.Dispose();
    }
}