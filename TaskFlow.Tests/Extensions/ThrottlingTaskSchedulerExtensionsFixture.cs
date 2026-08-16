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
        public async Task Enqueue_ShouldAdmitFirstOperation(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;

            var throttleTaskScheduler = taskFlow.WithThrottle(TimeSpan.FromSeconds(5), _timeProvider);

            var task = throttleTaskScheduler.Enqueue(() => 42);

            Assert.That(await task.ConfigureAwait(false), Is.EqualTo(42));
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public async Task Enqueue_ShouldRejectOperationInsideThrottleInterval(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;

            var throttleTaskScheduler = taskFlow.WithThrottle(TimeSpan.FromSeconds(5), _timeProvider);

            var admittedTask = throttleTaskScheduler.Enqueue(() => { });
            var throttledTask = throttleTaskScheduler.Enqueue(() => { });

            await admittedTask.ConfigureAwait(false);
            await Assert.ThatAsync(async () => await throttledTask.ConfigureAwait(false), Throws.TypeOf<OperationThrottledException>())
                .ConfigureAwait(false);
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public async Task Enqueue_ShouldAdmitOperationAtThrottleIntervalBoundary(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;

            var throttleTaskScheduler = taskFlow.WithThrottle(TimeSpan.FromSeconds(5), _timeProvider);

            await throttleTaskScheduler.Enqueue(() => { }).ConfigureAwait(false);
            _timeProvider.Advance(TimeSpan.FromSeconds(5));

            var task = throttleTaskScheduler.Enqueue(() => 42);

            Assert.That(await task.ConfigureAwait(false), Is.EqualTo(42));
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public async Task Enqueue_ShouldTreatZeroTimestampAsValidAdmissionTime(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;
            var timeProvider = new ZeroTimestampTimeProvider();
            var throttleTaskScheduler = taskFlow.WithThrottle(TimeSpan.FromSeconds(5), timeProvider);

            var admittedTask = throttleTaskScheduler.Enqueue(() => { });
            var throttledTask = throttleTaskScheduler.Enqueue(() => { });

            await admittedTask.ConfigureAwait(false);
            await Assert.ThatAsync(async () => await throttledTask.ConfigureAwait(false), Throws.TypeOf<OperationThrottledException>())
                .ConfigureAwait(false);
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public async Task Enqueue_ShouldConsumeIntervalWhenAdmittedOperationFails(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;
            var throttleTaskScheduler = taskFlow.WithThrottle(TimeSpan.FromSeconds(5), _timeProvider);

            var failedTask = throttleTaskScheduler.Enqueue(_ => Task.FromException(new InvalidOperationException("Expected test failure")));
            var throttledTask = throttleTaskScheduler.Enqueue(() => { });

            await Assert.ThatAsync(async () => await failedTask.ConfigureAwait(false), Throws.InvalidOperationException)
                .ConfigureAwait(false);
            await Assert.ThatAsync(async () => await throttledTask.ConfigureAwait(false), Throws.TypeOf<OperationThrottledException>())
                .ConfigureAwait(false);
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public async Task Enqueue_ShouldConsumeIntervalWhenAdmittedOperationIsCanceled(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;
            using var cancellationTokenSource = new CancellationTokenSource();
            await cancellationTokenSource.CancelAsync().ConfigureAwait(false);
            var throttleTaskScheduler = taskFlow.WithThrottle(TimeSpan.FromSeconds(5), _timeProvider);

            var canceledTask = throttleTaskScheduler.Enqueue(token => Task.Delay(Timeout.InfiniteTimeSpan, token), cancellationTokenSource.Token);
            var throttledTask = throttleTaskScheduler.Enqueue(() => { });

            await Assert.ThatAsync(async () => await canceledTask.ConfigureAwait(false), Throws.InstanceOf<OperationCanceledException>())
                .ConfigureAwait(false);
            await Assert.ThatAsync(async () => await throttledTask.ConfigureAwait(false), Throws.TypeOf<OperationThrottledException>())
                .ConfigureAwait(false);
        }

        private sealed class ZeroTimestampTimeProvider : TimeProvider
        {
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;

            public override long GetTimestamp() => 0;
        }
    }
}
