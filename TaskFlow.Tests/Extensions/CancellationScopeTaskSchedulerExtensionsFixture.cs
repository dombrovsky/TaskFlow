namespace TaskFlow.Tests.Extensions
{
    using NUnit.Framework;
    using System.Threading.Tasks.Flow;

    [TestFixture]
    public sealed class CancellationScopeTaskSchedulerExtensionsFixture
    {
        private ITaskFlow? _taskFlow;

        [TearDown]
        public void TearDown()
        {
            _taskFlow?.Dispose(TimeSpan.FromSeconds(1));
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public async Task SingleScope_Cancel_ShouldCancelOperationsThatBelongToScope(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;

            using var cts = new CancellationTokenSource();
            var cancellationScope = taskFlow.CreateCancellationScope(cts.Token);

            var task1 = cancellationScope.Enqueue(token => Task.Delay(1000, token));
            var task2 = _taskFlow.Enqueue(token => Task.Delay(1000, token));
            var task3 = cancellationScope.Enqueue(token => Task.Delay(1000, token));

            cts.Cancel();

            Assert.That(() => task1.IsCanceled, Is.True.After(100, 10), task1.Status.ToString);
            Assert.That(() => task2.IsCanceled, Is.False.After(100, 10), task2.Status.ToString);

            await task2.ConfigureAwait(false);
            Assert.That(task2.IsCompletedSuccessfully, Is.True);

            Assert.That(() => task3.IsCanceled, Is.True.After(100, 10), task1.Status.ToString);
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public async Task MultipleScopes_Cancel_ShouldCancelOperationsThatBelongToScope(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;

            using var scope1Cts = new CancellationTokenSource();
            using var scope2Cts = new CancellationTokenSource();
            var cancellationScope1 = taskFlow.CreateCancellationScope(scope1Cts.Token);
            var cancellationScope2 = taskFlow.CreateCancellationScope(scope2Cts.Token);

            var task1 = cancellationScope1.Enqueue(token => Task.Delay(1000, token));
            var task2 = cancellationScope2.Enqueue(token => Task.Delay(1000, token));
            var task3 = cancellationScope1.Enqueue(token => Task.Delay(1000, token));

            scope1Cts.Cancel();

            Assert.That(() => task1.IsCanceled, Is.True.After(100, 10), task1.Status.ToString);
            Assert.That(() => task2.IsCanceled, Is.False.After(100, 10), task2.Status.ToString);

            await task2.ConfigureAwait(false);
            Assert.That(task2.IsCompletedSuccessfully, Is.True);

            Assert.That(() => task3.IsCanceled, Is.True.After(100, 10), task1.Status.ToString);
        }
    }
}