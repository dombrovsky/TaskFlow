namespace TaskFlow.Tests.Extensions;

using NUnit.Framework;
using System.Threading.Tasks.Flow;

[CancelAfter(15000)]
internal sealed class TrySubmitFixture
{
    private static TaskCompletionSource<int> Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static BoundedAdmissionController Budget(int capacity = 1, TaskSchedulerCapacityMode mode = TaskSchedulerCapacityMode.Outstanding)
        => new(new BoundedAdmissionOptions { Capacity = capacity, CapacityMode = mode, MaxAdmissionWaiters = 1 });

    [TestCase(false)]
    [TestCase(true)]
    public async Task ImmediateDecision_SharesEnqueueCapacity_AndWaitStillAppliesBackpressure(bool dedicated)
    {
        await using TaskFlowBase flow = dedicated ? new DedicatedThreadTaskFlow() : new TaskFlow();
        var admission = Budget();
        var scheduler = flow.WithBoundedAdmission(admission).WithOperationName("event");
        var release = Gate();
        var started = Gate();
        Task<int> Hold(CancellationToken _)
        { started.TrySetResult(0); return release.Task; }
        var first = admission.TrySubmit(scheduler, Hold);
        try
        {
            Assert.That(first.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
            Assert.That(first.Completion, Is.Not.Null);
            await started.Task;
            var waiting = admission.Submit(scheduler, () => 2, TaskSchedulerAdmissionMode.Wait);
            var ran = false;
            var full = admission.TrySubmit(scheduler, () => { ran = true; return 3; });
            Assert.That(full.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Full));
            Assert.That(full.Completion, Is.Null);
            Assert.That(waiting.Admission.IsCompleted, Is.False);
            var overflow = admission.Submit(scheduler, () => 4, TaskSchedulerAdmissionMode.Wait);
            Assert.That(await overflow.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.WaiterLimitReached));
            await Assert.ThatAsync(async () => await overflow.Completion, Throws.TypeOf<AdmissionRejectedException>());
            release.SetResult(1);
            Assert.That(await first.Completion!, Is.EqualTo(1));
            Assert.That(await waiting.Completion, Is.EqualTo(2));
            Assert.That(ran, Is.False);
            Assert.That(await scheduler.Enqueue(() => 5), Is.EqualTo(5));
        }
        finally { release.TrySetResult(0); }
    }

    [TestCase(TaskSchedulerCapacityMode.Pending)]
    [TestCase(TaskSchedulerCapacityMode.Outstanding)]
    public async Task Accounting_ReleasesAtDelegateStartOrUnderlyingCompletion(TaskSchedulerCapacityMode mode)
    {
        await using var flow = new TaskFlow();
        var admission = Budget(mode: mode);
        var middleware = new ExecutionDelay();
        var scheduler = flow.WithBoundedAdmission(admission).UseMiddleware(middleware);
        var release = Gate();
        var started = Gate();
        Task<int> Hold(CancellationToken _)
        { started.TrySetResult(0); return release.Task; }
        var first = admission.TrySubmit(scheduler, Hold);
        try
        {
            await middleware.Entered.Task;
            Assert.That(admission.TrySubmit(scheduler, () => 0).Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Full));
            middleware.Release.SetResult(0);
            await started.Task;
            var second = admission.TrySubmit(scheduler, () => 2);
            Assert.That(second.Admission, Is.EqualTo(mode == TaskSchedulerCapacityMode.Pending ? TaskSchedulerAdmissionResult.Accepted : TaskSchedulerAdmissionResult.Full));
            Assert.That(admission.TrySubmit(scheduler, () => 0).Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Full));
            release.SetResult(1);
            await first.Completion!;
            if (second.Completion != null)
                Assert.That(await second.Completion, Is.EqualTo(2));
            Assert.That(await admission.TrySubmit(scheduler, () => 3).Completion!, Is.EqualTo(3));
        }
        finally { middleware.Release.TrySetResult(0); release.TrySetResult(0); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ConcurrentProducers_CannotExceedCapacity(bool coordinated)
    {
        await using var flow = new TaskFlow();
        var admission = Budget(7);
        ITaskScheduler scheduler = coordinated ? flow.WithConcurrencyLimit(2) : flow;
        scheduler = scheduler.WithBoundedAdmission(admission);
        var release = Gate();
        var start = Gate();
        Task<int> Hold(CancellationToken _) => release.Task;
        var producers = Enumerable.Range(0, 100).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            return admission.TrySubmit(scheduler, Hold);
        })).ToArray();
        try
        {
            start.SetResult(0);
            var results = await Task.WhenAll(producers);
            Assert.That(results.Count(s => s.Admission == TaskSchedulerAdmissionResult.Accepted), Is.EqualTo(7));
            Assert.That(results.Count(s => s.Admission == TaskSchedulerAdmissionResult.Full), Is.EqualTo(93));
            release.SetResult(42);
            Assert.That(await Task.WhenAll(results.Where(s => s.Completion != null).Select(s => s.Completion!)), Is.All.EqualTo(42));
            Assert.That(await admission.TrySubmit(scheduler, () => 43).Completion!, Is.EqualTo(43));
        }
        finally { start.TrySetResult(0); release.TrySetResult(0); }
    }

    [Test]
    public async Task TaskValueTaskStateAndCancellationOverloads_ExposeAcceptedOutcomes()
    {
        await using var flow = new TaskFlow();
        var admission = Budget();
        var observer = new Observer();
        var scheduler = flow.WithBoundedAdmission(admission).WithOperationName("save").UseMiddleware(observer);
        var state = new object();
        using var cancellation = new CancellationTokenSource();
        var result = admission.TrySubmit(scheduler, (object s, CancellationToken token) =>
        {
            Assert.That(s, Is.SameAs(state));
            Assert.That(token.CanBeCanceled, Is.True);
            return new ValueTask<int>(42);
        }, state, cancellation.Token);
        Assert.That(await result.Completion!, Is.EqualTo(42));
        Assert.That(observer.State, Is.SameAs(state));
        Assert.That(observer.Name, Is.EqualTo("save"));
        Assert.That(observer.Completions, Is.EqualTo(1));
        Task<int> TaskResult(CancellationToken _) => Task.FromResult(1);
        ValueTask<int> ValueResult(CancellationToken _) => new(2);
        Task TaskAction(CancellationToken _) => Task.CompletedTask;
        ValueTask ValueAction(CancellationToken _) => default;
        Assert.That(await admission.TrySubmit(scheduler, TaskResult, cancellation.Token).Completion!, Is.EqualTo(1));
        Assert.That(await admission.TrySubmit(scheduler, ValueResult).Completion!, Is.EqualTo(2));
        await admission.TrySubmit(scheduler, TaskAction).Completion!;
        await admission.TrySubmit(scheduler, ValueAction, cancellation.Token).Completion!;
        await admission.TrySubmit(scheduler, static (object _, CancellationToken _) => default(ValueTask), state).Completion!;
        await admission.TrySubmit(scheduler, (Action)(() => { })).Completion!;
        var failed = admission.TrySubmit(scheduler, (Func<int>)(() => throw new FormatException()));
        Assert.That(failed.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
        await Assert.ThatAsync(async () => await failed.Completion!, Throws.TypeOf<FormatException>());
        var canceled = admission.TrySubmit(scheduler, (Func<int>)(() => throw new OperationCanceledException()));
        Assert.That(canceled.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
        await Assert.ThatAsync(async () => await canceled.Completion!, Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task CancellationBeforeAdmissionThrows_AndAfterAdmissionUsesTerminalSemantics()
    {
        await using var flow = new TaskFlow();
        var admission = Budget(2);
        var scheduler = flow.WithBoundedAdmission(admission);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Assert.That(() => admission.TrySubmit(scheduler, () => 0, cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
        var release = Gate();
        var started = Gate();
        var first = scheduler.Enqueue((CancellationToken _) => { started.SetResult(0); return release.Task; });
        try
        {
            await started.Task;
            using var queuedCancellation = new CancellationTokenSource();
            Task<int> Cooperate(CancellationToken token)
            { token.ThrowIfCancellationRequested(); return Task.FromResult(2); }
            var queued = admission.TrySubmit(scheduler, Cooperate, queuedCancellation.Token);
            Assert.That(queued.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
            await queuedCancellation.CancelAsync();
            release.SetResult(1);
            await first;
            await Assert.ThatAsync(async () => await queued.Completion!, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(await admission.TrySubmit(scheduler, () => 3).Completion!, Is.EqualTo(3));
        }
        finally { release.TrySetResult(0); }
    }

    [Test]
    public async Task InlineBackend_NeverExecutesOnTheSynchronousProducerStack()
    {
        var backend = new InlineScheduler();
        await using var lifetime = new TaskSchedulerMiddlewareLifetime();
        var admission = Budget();
        var scheduler = new TaskFlowSchedulerAdapter(backend).WithBoundedAdmission(admission, lifetime);
        using var release = new ManualResetEventSlim();
        var started = Gate();
        var producer = Task.Run(() =>
        {
            return admission.TrySubmit(scheduler, () =>
            {
                started.SetResult(0);
                release.Wait();
                return 42;
            });
        });
        try
        {
            await started.Task;
            var submission = await producer.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(submission.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
            release.Set();
            Assert.That(await submission.Completion!, Is.EqualTo(42));
        }
        finally { release.Set(); }
    }

    [Test]
    public async Task RejectedCompletionMiddleware_RunsOffProducer_AndRemainsLifetimeOwned()
    {
        await using var flow = new TaskFlow();
        var admission = Budget();
        using var releaseCompletion = new ManualResetEventSlim();
        var completion = new BlockingRejectedCompletion(releaseCompletion);
        var scheduler = flow.WithBoundedAdmission(admission).UseMiddleware(completion);
        var release = Gate();
        var first = scheduler.Enqueue((CancellationToken _) => release.Task);
        try
        {
            var producer = Task.Run(() => admission.TrySubmit(scheduler, () => 2));
            await completion.Entered.Task;
            var rejected = await producer.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(rejected.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Full));
            Assert.That(rejected.Completion, Is.Null);
            var shutdown = flow.DisposeAsync().AsTask();
            release.SetResult(1);
            await first;
            Assert.That(shutdown.IsCompleted, Is.False);
            releaseCompletion.Set();
            await shutdown;
            Assert.That(admission.TrySubmit(scheduler, () => 3).Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Closed));
        }
        finally { releaseCompletion.Set(); release.TrySetResult(0); }
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task UnsupportedEnqueueMiddleware_IsRejectedBeforeAnyWork_ButSubmitRejectStillTraversesIt(bool outside)
    {
        await using var flow = new TaskFlow();
        var admission = Budget();
        var middleware = new DelayedEnqueue();
        var core = outside ? flow.WithBoundedAdmission(admission) : flow.UseMiddleware(middleware).WithBoundedAdmission(admission);
        var scheduler = outside ? core.UseMiddleware(middleware) : core;
        Assert.That(() => admission.TrySubmit(scheduler, () => 1), Throws.TypeOf<NotSupportedException>());
        Assert.That(middleware.Calls, Is.Zero);
        var submission = admission.Submit(scheduler, () => 2, TaskSchedulerAdmissionMode.Reject);
        Assert.That(middleware.Calls, Is.EqualTo(1));
        if (outside)
            Assert.That(submission.Admission.IsCompleted, Is.False);
        middleware.Release.SetResult(0);
        Assert.That(await submission.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
        Assert.That(await submission.Completion, Is.EqualTo(2));
    }

    [Test]
    public async Task Validation_RejectsOpaqueMissingDuplicateAndUncoordinatedMultipleBudgets()
    {
        await using var flow = new TaskFlow();
        var admission = Budget();
        var other = Budget();
        var scheduler = flow.WithBoundedAdmission(admission);
        Assert.That(() => admission.TrySubmit(flow, () => 0), Throws.TypeOf<ArgumentException>());
        Assert.That(() => other.TrySubmit(scheduler, () => 0), Throws.TypeOf<ArgumentException>());
        Assert.That(() => admission.TrySubmit(scheduler.WithBoundedAdmission(admission), () => 0), Throws.TypeOf<ArgumentException>());
        Assert.That(() => admission.TrySubmit(scheduler.WithBoundedAdmission(other), () => 0), Throws.TypeOf<NotSupportedException>());
        Assert.That(() => admission.TrySubmit(scheduler, (Action)null!), Throws.TypeOf<ArgumentNullException>());
        Assert.That(() => admission.TrySubmit(null!, () => 0), Throws.TypeOf<ArgumentNullException>());
        Assert.That(() => ((BoundedAdmissionController)null!).TrySubmit(scheduler, () => 0), Throws.TypeOf<ArgumentNullException>());
        await using var lifetime = new TaskSchedulerMiddlewareLifetime();
        var opaque = new OpaqueTerminal().WithBoundedAdmission(admission, lifetime);
        Assert.That(() => admission.TrySubmit(opaque, () => 0), Throws.TypeOf<NotSupportedException>());
        Assert.That(await admission.TrySubmit(scheduler, () => 42).Completion!, Is.EqualTo(42));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SchedulingFailures_AreObservableOnAcceptedWork_AndReleaseReservations(bool keyed)
    {
        var backend = new InlineScheduler { Reject = true };
        await using var flow = new TaskFlow(new TaskFlowOptions { TaskScheduler = backend });
        var admission = Budget();
        var scheduler = keyed ? flow.WithKeyedScheduling<string>().WithConcurrencyLimit(1).WithBoundedAdmission(admission)
            : new TaskFlowSchedulerAdapter(backend).WithBoundedAdmission(admission, flow.MiddlewareLifetime);
        var failed = keyed ? admission.TrySubmit(scheduler, "A", _ => 1) : admission.TrySubmit(scheduler, () => 1);
        Assert.That(failed.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
        await Assert.ThatAsync(async () => await failed.Completion!, Throws.TypeOf<TaskSchedulerException>());
        backend.Reject = false;
        var replacement = keyed ? admission.TrySubmit(scheduler, "A", _ => 2) : admission.TrySubmit(scheduler, () => 2);
        Assert.That(await replacement.Completion!, Is.EqualTo(2));
    }

    [Test]
    public async Task ConcurrentBranch_ComposesMultipleBudgetsAtomically()
    {
        await using var flow = new TaskFlow();
        var global = Budget(2);
        var smaller = Budget();
        var root = flow.WithConcurrencyLimit(1).WithBoundedAdmission(global);
        var scheduler = root.WithBoundedAdmission(smaller);
        var release = Gate();
        var first = global.TrySubmit(scheduler, (CancellationToken _) => release.Task);
        try
        {
            Assert.That(first.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
            Assert.That(smaller.TrySubmit(scheduler, () => 2).Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Full));
            // The rejected combined submission must not reserve the second global slot.
            var other = global.TrySubmit(root, () => 3);
            Assert.That(other.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
            release.SetResult(1);
            Assert.That(await other.Completion!, Is.EqualTo(3));
            await first.Completion!;
            Assert.That(await global.TrySubmit(scheduler, () => 4).Completion!, Is.EqualTo(4));
        }
        finally { release.TrySetResult(0); }
    }

    [TestCase(TaskSchedulerCapacityMode.Pending)]
    [TestCase(TaskSchedulerCapacityMode.Outstanding)]
    public async Task CompletionDelay_RetainsKeyExecutionSlotAndOutstandingCapacity(TaskSchedulerCapacityMode mode)
    {
        await using var flow = new TaskFlow();
        var admission = Budget(mode: mode);
        var completion = new CompletionDelay();
        var scheduler = flow.WithKeyedScheduling<string>().WithConcurrencyLimit(1).WithBoundedAdmission(admission).UseMiddleware(completion);
        var first = admission.TrySubmit(scheduler, "A", _ => 1);
        try
        {
            await completion.Entered.Task;
            var ran = false;
            var second = admission.TrySubmit(scheduler, "B", _ => { ran = true; return 2; });
            Assert.That(second.Admission, Is.EqualTo(mode == TaskSchedulerCapacityMode.Pending ? TaskSchedulerAdmissionResult.Accepted : TaskSchedulerAdmissionResult.Full));
            Assert.That(ran, Is.False);
            completion.Release.SetResult(0);
            Assert.That(await first.Completion!, Is.EqualTo(1));
            if (second.Completion != null)
                Assert.That(await second.Completion, Is.EqualTo(2));
            var selfDispose = admission.TrySubmit(scheduler, "A", (string _, CancellationToken _) => flow.DisposeAsync().AsTask());
            await Assert.ThatAsync(async () => await selfDispose.Completion!, Throws.TypeOf<InvalidOperationException>());
        }
        finally { completion.Release.TrySetResult(0); }
    }

    [Test]
    public async Task BuiltInEnqueueDecorators_AreExplicitlyUnsupported()
    {
        await using var flow = new TaskFlow();
        var admission = Budget();
        var scheduler = flow.WithKeyedScheduling<string>().WithBoundedAdmission(admission);
        using var cancellation = new CancellationTokenSource();
        ITaskScheduler[] unsupported = [scheduler.WithTimeout(TimeSpan.FromSeconds(1)), scheduler.CreateCancellationScope(cancellation.Token), scheduler.CreateCancelPrevious()];
        foreach (var decorated in unsupported)
            Assert.That(() => admission.TrySubmit(decorated, "A", _ => 0), Throws.TypeOf<NotSupportedException>());
        Assert.That(await admission.TrySubmit(scheduler, "A", _ => 42).Completion!, Is.EqualTo(42));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task TokenlessAsyncDelegates_AwaitTheirWorkInsteadOfReturningNestedTasks(bool keyed)
    {
        await using var flow = new TaskFlow();
        var admission = Budget();
        var scheduler = (keyed ? flow.WithKeyedScheduling<string>() : (ITaskScheduler)flow).WithBoundedAdmission(admission);
        for (var kind = 0; kind < 4; kind++)
        {
            var release = Gate();
            Task<int> TaskResult() => release.Task;
            ValueTask<int> ValueResult() => new(release.Task);
            Task TaskAction() => release.Task;
            ValueTask ValueAction() => new(release.Task);
            Task<int> KeyedTaskResult(string key) => release.Task;
            ValueTask<int> KeyedValueResult(string key) => new(release.Task);
            Task KeyedTaskAction(string key) => release.Task;
            ValueTask KeyedValueAction(string key) => new(release.Task);
            TaskSchedulerTrySubmission submission = (keyed, kind) switch
            {
                (false, 0) => admission.TrySubmit(scheduler, TaskResult),
                (false, 1) => admission.TrySubmit(scheduler, ValueResult, CancellationToken.None),
                (false, 2) => admission.TrySubmit(scheduler, TaskAction),
                (false, 3) => admission.TrySubmit(scheduler, ValueAction, CancellationToken.None),
                (true, 0) => admission.TrySubmit(scheduler, "A", KeyedTaskResult, CancellationToken.None),
                (true, 1) => admission.TrySubmit(scheduler, "A", KeyedValueResult),
                (true, 2) => admission.TrySubmit(scheduler, "A", KeyedTaskAction, CancellationToken.None),
                (true, 3) => admission.TrySubmit(scheduler, "A", KeyedValueAction),
                _ => throw new AssertionException("Unknown overload")
            };
            try
            {
                Assert.That(submission.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
                Assert.That(submission.Completion!.IsCompleted, Is.False);
                var full = keyed ? admission.TrySubmit(scheduler, "B", _ => 0).Admission : admission.TrySubmit(scheduler, () => 0).Admission;
                Assert.That(full, Is.EqualTo(TaskSchedulerAdmissionResult.Full));
                release.SetResult(42);
                await submission.Completion;
                if (kind < 2)
                {
                    Assert.That(submission, Is.InstanceOf<TaskSchedulerTrySubmission<int>>());
                    Assert.That(await ((TaskSchedulerTrySubmission<int>)submission).Completion!, Is.EqualTo(42));
                }
            }
            finally { release.TrySetResult(0); }
        }
    }

    private sealed class CompletionDelay : ITaskSchedulerCompletionMiddleware
    {
        public TaskCompletionSource<int> Entered { get; } = Gate();
        public TaskCompletionSource<int> Release { get; } = Gate();
        public async ValueTask<TaskSchedulerOperationOutcome<T>> InvokeAsync<T>(TaskSchedulerOperationContext context, TaskSchedulerOperationOutcome<T> outcome, TaskSchedulerCompletionDelegate<T> next)
        {
            Entered.TrySetResult(0);
            await Release.Task;
            return await next(context, outcome);
        }
    }

    private sealed class ExecutionDelay : ITaskSchedulerExecutionMiddleware
    {
        public TaskCompletionSource<int> Entered { get; } = Gate();
        public TaskCompletionSource<int> Release { get; } = Gate();
        public async ValueTask<T> InvokeAsync<T>(TaskSchedulerOperationContext context, TaskSchedulerExecutionDelegate<T> next)
        {
            Entered.TrySetResult(0);
            await Release.Task;
            return await next(context);
        }
    }

    private sealed class Observer : ITaskSchedulerExecutionMiddleware, ITaskSchedulerCompletionMiddleware
    {
        public object? State { get; private set; }
        public string? Name { get; private set; }
        public int Completions { get; private set; }
        public ValueTask<T> InvokeAsync<T>(TaskSchedulerOperationContext context, TaskSchedulerExecutionDelegate<T> next)
        {
            State = context.State;
            Name = context.GetAnnotation<OperationNameAnnotation>()?.OperationName;
            return next(context);
        }
        public ValueTask<TaskSchedulerOperationOutcome<T>> InvokeAsync<T>(TaskSchedulerOperationContext context, TaskSchedulerOperationOutcome<T> outcome, TaskSchedulerCompletionDelegate<T> next)
        {
            Completions++;
            return next(context, outcome);
        }
    }

    private sealed class BlockingRejectedCompletion(ManualResetEventSlim release) : ITaskSchedulerCompletionMiddleware
    {
        public TaskCompletionSource<int> Entered { get; } = Gate();
        public ValueTask<TaskSchedulerOperationOutcome<T>> InvokeAsync<T>(TaskSchedulerOperationContext context, TaskSchedulerOperationOutcome<T> outcome, TaskSchedulerCompletionDelegate<T> next)
        {
            if (outcome.Exception is AdmissionRejectedException)
            {
                Entered.SetResult(0);
                release.Wait();
                throw new FormatException("Observe this internal rejection failure");
            }
            return next(context, outcome);
        }
    }

    private sealed class DelayedEnqueue : ITaskSchedulerEnqueueMiddleware
    {
        public int Calls { get; private set; }
        public TaskCompletionSource<int> Release { get; } = Gate();
        public async Task<T> InvokeAsync<T>(TaskSchedulerEnqueueContext<T> context, TaskSchedulerEnqueueDelegate<T> next)
        {
            Calls++;
            await Release.Task;
            return await next(context);
        }
    }

    private sealed class InlineScheduler : TaskScheduler
    {
        public bool Reject { get; set; }
        protected override IEnumerable<Task> GetScheduledTasks() => Array.Empty<Task>();
        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => TryExecuteTask(task);
        protected override void QueueTask(Task task)
        {
            if (Reject)
                throw new InvalidOperationException("Scheduling refused");
            TryExecuteTask(task);
        }
    }

    private sealed class OpaqueTerminal : ITaskScheduler
    {
        public Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
            => throw new AssertionException("Validation must reject this terminal before submission");
    }
}