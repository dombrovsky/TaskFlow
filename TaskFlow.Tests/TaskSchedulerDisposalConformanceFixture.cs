namespace TaskFlow.Tests;

using NUnit.Framework;
using System.Threading.Tasks.Flow;

[TestFixtureSource(nameof(Scenarios))]
[CancelAfter(10000)]
internal sealed class TaskSchedulerDisposalConformanceFixture
{
    private readonly string _implementation;
    private readonly string _configuration;

    private ITaskFlow? _flow;
    private DisposableTaskScheduler? _adapterOwner;
    private TaskSchedulerMiddlewareLifetime? _lifetime;
    private ITaskScheduler _scheduler = null!;
    private PhaseObserver _observer = null!;

    public TaskSchedulerDisposalConformanceFixture(string implementation, string configuration)
    {
        _implementation = implementation;
        _configuration = configuration;
    }

    public static IEnumerable<TestFixtureData> Scenarios()
    {
        foreach (var implementation in new[] { "TaskFlow", "DedicatedThread", "CurrentThread" })
            foreach (var configuration in new[] { "Plain", "Annotated", "Pipeline", "ExtendedState", "Bounded" })
                yield return new TestFixtureData(implementation, configuration);
        foreach (var configuration in new[] { "Concurrent", "Keyed", "KeyedBounded" })
            yield return new TestFixtureData("TaskFlow", configuration);
        yield return new TestFixtureData("Adapter", "Plain");
        yield return new TestFixtureData("Adapter", "ExplicitLifetime");
    }

    [SetUp]
    public async Task SetUp()
    {
        _observer = new PhaseObserver();
        _flow = null;
        _adapterOwner = null;
        _lifetime = null;
        if (_implementation == "Adapter")
        {
            _adapterOwner = new DisposableTaskScheduler();
            _scheduler = new TaskFlowSchedulerAdapter(_adapterOwner);
        }
        else
        {
            _flow = _implementation switch
            {
                "TaskFlow" => new TaskFlow(),
                "DedicatedThread" => new DedicatedThreadTaskFlow(),
                "CurrentThread" => StartCurrentThread(),
                _ => throw new ArgumentOutOfRangeException(nameof(_implementation)),
            };
            _scheduler = _flow;
        }

        _scheduler = _configuration switch
        {
            "Plain" => _scheduler,
            "Annotated" => _scheduler.WithOperationName("disposal-conformance"),
            "Pipeline" => _scheduler.UseMiddleware(_observer),
            "ExtendedState" => _scheduler.WithExtendedState(42),
            "Bounded" => _scheduler.WithBoundedAdmission(new BoundedAdmissionOptions { Capacity = 1 }).UseMiddleware(_observer),
            "Concurrent" => _scheduler.WithConcurrencyLimit(1).UseMiddleware(_observer),
            "Keyed" => _scheduler.WithKeyedScheduling<string>().UseMiddleware(_observer),
            "KeyedBounded" => _scheduler.WithKeyedScheduling<string>().WithPerKeyBoundedAdmission(new BoundedAdmissionOptions { Capacity = 1 }).UseMiddleware(_observer),
            "ExplicitLifetime" => WithExplicitLifetime(_scheduler),
            _ => throw new ArgumentOutOfRangeException(nameof(_configuration)),
        };
        // Exercise the same API before disposal so a permanently rejecting scheduler cannot pass.
        await TaskSchedulerDisposalConformance.Enqueue(_scheduler, "DirectGeneric", () => { }, IsKeyed);
        _observer.Invocations = 0;
    }

    private bool IsKeyed => _configuration is "Keyed" or "KeyedBounded";

    [TearDown]
    public async Task TearDown() => await DisposeOwnerAsync();

    [TestCaseSource(typeof(TaskSchedulerDisposalConformance), nameof(TaskSchedulerDisposalConformance.Overloads))]
    public void Enqueue_AfterDispose_ShouldRejectSynchronously(string overload)
    {
        DisposeOwner();
        TaskSchedulerDisposalConformance.AssertRejected(_scheduler, overload, IsKeyed);
        Assert.That(_observer.Invocations, Is.Zero, "Disposed middleware must not execute any phase.");
    }

    [TestCaseSource(typeof(TaskSchedulerDisposalConformance), nameof(TaskSchedulerDisposalConformance.Overloads))]
    public async Task Enqueue_AfterDisposeAsync_ShouldRejectSynchronously(string overload)
    {
        await DisposeOwnerAsync();
        TaskSchedulerDisposalConformance.AssertRejected(_scheduler, overload, IsKeyed);
        Assert.That(_observer.Invocations, Is.Zero, "Disposed middleware must not execute any phase.");
    }

    [TestCaseSource(typeof(TaskSchedulerDisposalConformance), nameof(TaskSchedulerDisposalConformance.Overloads))]
    public async Task Enqueue_AfterShutdownStarts_ShouldRejectSynchronously(string overload)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Operation()
        {
            started.SetResult();
            await release.Task.ConfigureAwait(false);
        }
        var held = IsKeyed ? _scheduler.Enqueue<string>("key", Operation) : _scheduler.Enqueue(Operation);
        Task? shutdown = null;
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var invocations = _observer.Invocations;
            shutdown = DisposeOwnerAsync();
            TaskSchedulerDisposalConformance.AssertRejected(_scheduler, overload, IsKeyed);
            Assert.That(_observer.Invocations, Is.EqualTo(invocations), "Entry rejection must not invoke middleware while accepted work drains.");
        }
        finally
        {
            release.TrySetResult();
            if (shutdown != null)
                await shutdown;
            await held;
        }
    }

    [TestCaseSource(typeof(TaskSchedulerDisposalConformance), nameof(TaskSchedulerDisposalConformance.Overloads))]
    public void Enqueue_NullDelegate_ShouldThrowSynchronously(string overload)
    {
        Task? task = null;
        Assert.That(() => { task = TaskSchedulerDisposalConformance.EnqueueNullDelegate(_scheduler, overload, IsKeyed); }, Throws.TypeOf<ArgumentNullException>());
        Assert.That(task, Is.Null);
        Assert.That(_observer.Invocations, Is.Zero, "Invalid arguments must be rejected before middleware executes.");
    }

    [TestCase("DirectGeneric", "disposed")]
    [TestCase("DirectGeneric", "argument")]
    [TestCase("Action", "disposed")]
    [TestCase("Action", "argument")]
    public async Task Enqueue_AcceptedOperationThrows_ShouldFaultTask(string overload, string exceptionType)
    {
        Exception failure = exceptionType == "disposed" ? new ObjectDisposedException("operation-resource") : new ArgumentException("Operation failed");
        Task? task = null;
        Assert.That(() => { task = TaskSchedulerDisposalConformance.Enqueue(_scheduler, overload, () => throw failure, IsKeyed); }, Throws.Nothing);
        await Assert.ThatAsync(async () => await task!, Throws.Exception.SameAs(failure));
        Assert.That(task!.IsFaulted, Is.True);
    }

    private ITaskScheduler WithExplicitLifetime(ITaskScheduler scheduler)
    {
        _lifetime = new TaskSchedulerMiddlewareLifetime();
        return scheduler.WithBoundedAdmission(new BoundedAdmissionController(new BoundedAdmissionOptions { Capacity = 1 }), _lifetime).UseMiddleware(_observer);
    }

    private void DisposeOwner()
    {
        _lifetime?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _flow?.Dispose();
        _adapterOwner?.Dispose();
    }

    private async Task DisposeOwnerAsync()
    {
        if (_lifetime != null)
            await _lifetime.DisposeAsync();
        if (_flow != null)
            await _flow.DisposeAsync();
        _adapterOwner?.Dispose();
    }

    private static CurrentThreadTaskFlow StartCurrentThread()
    {
        var flow = new CurrentThreadTaskFlow();
        new Thread(flow.Run) { IsBackground = true }.Start();
        return flow;
    }

    private sealed class PhaseObserver : ITaskSchedulerEnqueueMiddleware, ITaskSchedulerExecutionMiddleware, ITaskSchedulerCompletionMiddleware
    {
        public int Invocations { get; set; }
        public Task<T> InvokeAsync<T>(TaskSchedulerEnqueueContext<T> context, TaskSchedulerEnqueueDelegate<T> next)
        {
            Invocations++;
            return next(context);
        }
        public ValueTask<T> InvokeAsync<T>(TaskSchedulerOperationContext context, TaskSchedulerExecutionDelegate<T> next)
        {
            Invocations++;
            return next(context);
        }
        public ValueTask<TaskSchedulerOperationOutcome<T>> InvokeAsync<T>(TaskSchedulerOperationContext context, TaskSchedulerOperationOutcome<T> outcome, TaskSchedulerCompletionDelegate<T> next)
        {
            Invocations++;
            return next(context, outcome);
        }
    }

    private sealed class DisposableTaskScheduler : TaskScheduler, IDisposable
    {
        private int _disposed;

        public void Dispose() => Volatile.Write(ref _disposed, 1);
        protected override void QueueTask(Task task)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            ThreadPool.QueueUserWorkItem(_ => TryExecuteTask(task));
        }
        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;
        protected override IEnumerable<Task> GetScheduledTasks() => [];
    }
}