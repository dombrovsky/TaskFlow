namespace TaskFlow.Tests.Extensions
{
    using Microsoft.Extensions.Time.Testing;
    using NUnit.Framework;
    using System.Threading.Tasks.Flow;

    [TestFixture]
    internal sealed class ThrottlingTaskSchedulerExtensionsFixture
    {
        private ITaskFlow? _taskFlow;
        private FakeTimeProvider _timeProvider;

        [SetUp]
        public void Setup()
        {
            _timeProvider = new FakeTimeProvider();
        }

        [TearDown]
        public void TearDown()
        {
            _taskFlow?.Dispose(TimeSpan.FromSeconds(1));
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public async Task Enqueue_ShouldExecuteOnlyIfDebounceIntervalPassed(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;

            var debounceTaskScheduler = taskFlow.WithDebounce(TimeSpan.FromSeconds(5), _timeProvider);

            var counter = 0;
            for (var i = 0; i < 10; i++)
            {
                _ = debounceTaskScheduler.Enqueue(() => Interlocked.Increment(ref counter));
                _timeProvider.Advance(TimeSpan.FromSeconds(1));
            }

            await _taskFlow.Enqueue(() => { });

            Assert.That(counter, Is.EqualTo(2));
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public async Task Enqueue_ShouldThrowIfDebounceIntervalNotPassed(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;

            var debounceTaskScheduler = taskFlow.WithDebounce(TimeSpan.FromSeconds(5), _timeProvider);

            var task1 = debounceTaskScheduler.Enqueue(() => { });
            var task2 = debounceTaskScheduler.Enqueue(() => { });

            await _taskFlow.Enqueue(() => { });

            await task1.ConfigureAwait(false);
            Assert.That(task1.IsCompletedSuccessfully, Is.True);
            await Assert.ThatAsync(async () => await task2.ConfigureAwait(false), Throws.TypeOf<OperationThrottledException>())
                .ConfigureAwait(false);
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public async Task Enqueue_WhenAcceptedOperationFails_ShouldStillThrottleNextOperationWithinInterval(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;

            var debounceTaskScheduler = taskFlow.WithDebounce(TimeSpan.FromSeconds(5), _timeProvider);

            var failedTask = debounceTaskScheduler.Enqueue(_ => Task.FromException(new InvalidOperationException("boom")));
            var throttledTask = debounceTaskScheduler.Enqueue(() => 42);

            await Assert.ThatAsync(async () => await failedTask.ConfigureAwait(false), Throws.TypeOf<InvalidOperationException>())
                .ConfigureAwait(false);
            await Assert.ThatAsync(async () => await throttledTask.ConfigureAwait(false), Throws.TypeOf<OperationThrottledException>())
                .ConfigureAwait(false);
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public async Task Enqueue_WhenAcceptedOperationIsCanceled_ShouldStillThrottleNextOperationWithinInterval(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;

            var debounceTaskScheduler = taskFlow.WithDebounce(TimeSpan.FromSeconds(5), _timeProvider);
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync().ConfigureAwait(false);

            var canceledTask = debounceTaskScheduler.Enqueue(Task.FromCanceled, cts.Token);
            var throttledTask = debounceTaskScheduler.Enqueue(() => 42);

            await Assert.ThatAsync(async () => await canceledTask.ConfigureAwait(false), Throws.InstanceOf<OperationCanceledException>())
                .ConfigureAwait(false);
            await Assert.ThatAsync(async () => await throttledTask.ConfigureAwait(false), Throws.TypeOf<OperationThrottledException>())
                .ConfigureAwait(false);
        }
    }
}
