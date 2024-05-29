namespace TaskFlow.Tests.Extensions
{
    using NUnit.Framework;
    using System.Threading.Tasks.Flow;

    [TestFixture]
    public sealed class TimeoutTaskSchedulerExtensionsFixture
    {
        private ITaskFlow? _taskFlow;

        [TearDown]
        public void TearDown()
        {
            _taskFlow?.Dispose(TimeSpan.FromSeconds(1));
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public void Timeout_ShouldThrowTimeoutException(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;

            var task = taskFlow
                .WithTimeout(TimeSpan.FromMilliseconds(100))
                .Enqueue(token => Task.Delay(1000, token));

            Assert.That(async () => await task.ConfigureAwait(false), Throws.InstanceOf<TimeoutException>());
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public void Timeout_CancelsTask(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;
            CancellationToken actualCancellationToken = default;
            var task = taskFlow
                .WithTimeout(TimeSpan.FromMilliseconds(100))
                .Enqueue(token =>
                {
                    actualCancellationToken = token;
                    return Task.Delay(1000, token);
                });

            Assert.That(async () => await task.ConfigureAwait(false), Throws.InstanceOf<TimeoutException>());
            Assert.That(actualCancellationToken.IsCancellationRequested, Is.True);
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public void NoTimeout_ShouldNotThrowTimeoutException(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;

            var task = taskFlow
                .WithTimeout(TimeSpan.FromMilliseconds(2000))
                .Enqueue(token => Task.Delay(1000, token));

            Assert.That(async () => await task.ConfigureAwait(false), Throws.Nothing);
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public void InfiniteTimeout_ShouldNotThrowTimeoutException(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;

            var task = taskFlow
                .WithTimeout(Timeout.InfiniteTimeSpan)
                .Enqueue(token => Task.Delay(1000, token));

            Assert.That(async () => await task.ConfigureAwait(false), Throws.Nothing);
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public void Timeout_WhenOperationNameSpecified_ShouldThrowTimeoutExceptionWithOperationName(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;

            var task = taskFlow
                .WithTimeout(TimeSpan.FromMilliseconds(100))
                .WithOperationName("inner")
                .CreateCancelPrevious()
                .WithOperationName("outer")
                .Enqueue(
                    async (state, token) =>
                    {
                        await Task.Delay(1000, token).ConfigureAwait(false);
                        return state;
                    },
                    42,
                    CancellationToken.None);

            Assert.That(async () => await task.ConfigureAwait(false), Throws.InstanceOf<TimeoutException>().With.Message.Contain("outer"));
        }
    }
}