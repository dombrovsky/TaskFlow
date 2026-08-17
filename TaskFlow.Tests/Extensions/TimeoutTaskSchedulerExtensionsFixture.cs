namespace TaskFlow.Tests.Extensions
{
    using NUnit.Framework;
    using System.Threading.Tasks.Flow;

    [TestFixture]
    internal sealed class TimeoutTaskSchedulerExtensionsFixture
    {
        private static readonly string[] CompletionBoundaryOutcomes = ["success", "timeout"];
        private static readonly string[] CancellationBoundaryOutcomes = ["canceled", "timeout"];
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
                .WithOperationName("inner")
                .WithTimeout(TimeSpan.FromMilliseconds(100))
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

            Assert.That(async () => await task.ConfigureAwait(false), Throws.InstanceOf<TimeoutException>().With.Message.Contain("inner"));
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public async Task Timeout_WhenExternalCancellationComesFirst_ShouldThrowOperationCanceledException(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;
            using var cts = new CancellationTokenSource();

            var task = taskFlow
                .WithTimeout(TimeSpan.FromSeconds(2))
                .Enqueue(token => Task.Delay(Timeout.InfiniteTimeSpan, token), cts.Token);

            await cts.CancelAsync().ConfigureAwait(false);

            await Assert.ThatAsync(
                    async () => await task.ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>())
                .ConfigureAwait(false);
            Assert.That(task.IsCanceled, Is.True);
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        [CancelAfter(5000)]
        public async Task Timeout_WhenOperationCompletesAtBoundary_ShouldOnlyProduceSuccessOrTimeout(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;
            var timeout = TimeSpan.FromMilliseconds(40);
            var outcomes = new HashSet<string>();

            for (var i = 0; i < 25; i++)
            {
                var task = taskFlow
                    .WithTimeout(timeout)
                    .Enqueue(async token =>
                    {
                        await Task.Delay(timeout, token).ConfigureAwait(false);
                        return 42;
                    });

                outcomes.Add(await ClassifyBoundaryResult(task).ConfigureAwait(false));
            }

            Assert.That(outcomes, Is.Not.Empty);
            Assert.That(outcomes, Is.SubsetOf(CompletionBoundaryOutcomes));
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        [CancelAfter(5000)]
        public async Task Timeout_WhenExternalCancellationCompetesWithTimeoutBoundary_ShouldOnlyProduceCanceledOrTimeout(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;
            var timeout = TimeSpan.FromMilliseconds(40);
            var outcomes = new HashSet<string>();

            for (var i = 0; i < 25; i++)
            {
                using var cts = new CancellationTokenSource(timeout);
                var task = taskFlow
                    .WithTimeout(timeout)
                    .Enqueue(token => Task.Delay(Timeout.InfiniteTimeSpan, token), cts.Token);

                outcomes.Add(await ClassifyBoundaryResult(task).ConfigureAwait(false));
            }

            Assert.That(outcomes, Is.Not.Empty);
            Assert.That(outcomes, Is.SubsetOf(CancellationBoundaryOutcomes));
        }

        private static async Task<string> ClassifyBoundaryResult<T>(Task<T> task)
        {
            try
            {
                _ = await task.ConfigureAwait(false);
                return "success";
            }
            catch (TimeoutException)
            {
                return "timeout";
            }
            catch (OperationCanceledException)
            {
                return "canceled";
            }
        }

        private static async Task<string> ClassifyBoundaryResult(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
                return "success";
            }
            catch (TimeoutException)
            {
                return "timeout";
            }
            catch (OperationCanceledException)
            {
                return "canceled";
            }
        }
    }
}
