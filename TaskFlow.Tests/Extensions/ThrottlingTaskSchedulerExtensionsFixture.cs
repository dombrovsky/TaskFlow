namespace TaskFlow.Tests.Extensions
{
    using Microsoft.Extensions.Time.Testing;
    using NUnit.Framework;
    using System.Threading.Tasks.Flow;

    [TestFixture]
    public sealed class ThrottlingTaskSchedulerExtensionsFixture
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

            Assert.That(task1.IsCompletedSuccessfully, Is.True);
            Assert.That(async () => await task2, Throws.TypeOf<OperationThrottledException>());
        }
    }
}