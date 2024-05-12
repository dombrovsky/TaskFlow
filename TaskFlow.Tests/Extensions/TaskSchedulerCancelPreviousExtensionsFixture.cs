namespace TaskFlow.Tests.Extensions
{
    using NUnit.Framework;
    using System.Threading.Tasks.Flow;

    [TestFixture]
    public sealed class TaskSchedulerCancelPreviousExtensionsFixture
    {
        private ITaskFlow? _taskFlow;

        [TearDown]
        public void TearDown()
        {
            _taskFlow?.Dispose(TimeSpan.FromSeconds(1));
        }

        [TestCaseSource(nameof(CreateTaskFlows))]
        public async Task Enqueue_ShouldCancelPreviousOperation(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;

            var cancelPrevious = taskFlow.CreateCancelPrevious();

            var task1 = cancelPrevious.Enqueue(token => Task.Delay(1000, token));
            Assert.That(task1.IsCompleted, Is.False);

            var task2 = cancelPrevious.Enqueue(token => Task.Delay(1000, token));
            Assert.That(() => task1.IsCanceled, Is.True.After(100, 10), task1.Status.ToString);
            Assert.That(task2.IsCompleted, Is.False);

            var task3 = cancelPrevious.Enqueue(token => Task.Delay(1000, token));
            Assert.That(() => task2.IsCanceled, Is.True.After(100, 10), task2.Status.ToString);
            Assert.That(task3.IsCompleted, Is.False);


            await task3.ConfigureAwait(false);
            Assert.That(task3.IsCompletedSuccessfully, Is.True);
        }

        [TestCaseSource(nameof(CreateTaskFlows))]
        public async Task Enqueue_ShouldNotCancelPreviousOperations_OnParentTaskScheduler(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;

            var parentTaskScheduler = (ITaskScheduler)taskFlow;
            var cancelPrevious = taskFlow.CreateCancelPrevious();

            var task1 = parentTaskScheduler.Enqueue(token => Task.Delay(1000, token));
            Assert.That(task1.IsCompleted, Is.False);

            var task2 = cancelPrevious.Enqueue(token => Task.Delay(1000, token));
            Assert.That(() => task1.IsCanceled, Is.False.After(100, 10), task1.Status.ToString);
            Assert.That(task2.IsCompleted, Is.False);

            await task1.ConfigureAwait(false);

            var task3 = cancelPrevious.Enqueue(token => Task.Delay(1000, token));
            Assert.That(() => task2.IsCanceled, Is.True.After(100, 10), task2.Status.ToString);
            Assert.That(task3.IsCompleted, Is.False);

            await task3.ConfigureAwait(false);
            Assert.That(task1.IsCompletedSuccessfully, Is.True);
            Assert.That(task2.IsCanceled, Is.True);
            Assert.That(task3.IsCompletedSuccessfully, Is.True);
        }

        private static IEnumerable<ITaskFlow> CreateTaskFlows()
        {
            yield return new TaskFlow();
            yield return new DedicatedThreadTaskFlow();
        }
    }
}
