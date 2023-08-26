namespace TaskFlow.Tests
{
    using NUnit.Framework;
    using System.Threading.Tasks.Flow;

    public abstract partial class TaskSchedulerBaseFixture<T>
    {
        [Test]
        public async Task Enqueue_ShouldExecuteInScheduledOrder()
        {
            var counter = 0;
            var tasks = Enumerable.Range(0, 10)
                .Select(i => _sut.Enqueue(() => Interlocked.Increment(ref counter) == i + 1).AsTask()).ToArray();

            for (var i = 0; i < tasks.Length; i++)
            {
                for (var j = 0; j < i; j++)
                {
                    Assert.That(tasks[j].IsCompleted, Is.True);
                }

                Assert.That(await tasks[i].ConfigureAwait(false), Is.True);
            }
        }

        [Test]
        public void Enqueue_ReturnedTaskShouldCompleteWhenTaskFuncComplete()
        {
            using var completedEvent = new ManualResetEventSlim();

            var task = _sut.Enqueue(
                () =>
                {
                    Thread.Sleep(100);
                    completedEvent.Set();
                }).AsTask();

            Assert.That(completedEvent.Wait(0), Is.False);
            Assert.That(task.Wait(200), Is.True);
            Assert.That(completedEvent.Wait(0), Is.True);
        }

        [Test]
        public void Enqueue_ReturnedTaskShouldBeFailedWhenTaskFuncFailed()
        {
            var task = _sut.Enqueue(_ => Task.FromException(new InvalidOperationException("Failure"))).AsTask();

            Assert.That(async () => await task.ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.EqualTo("Failure"));
            Assert.That(task.IsFaulted, Is.True);
        }

        [Test]
        public void Enqueue_ReturnedTaskShouldBeCanceledWhenTaskFuncCanceled()
        {
            using var cts = new CancellationTokenSource();
            var task = _sut.Enqueue(token => Task.Delay(1000, token), cts.Token).AsTask();

            cts.Cancel();

            Assert.That(async () => await task.ConfigureAwait(false), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(task.IsCanceled, Is.True);
        }

        [Test]
        public void Enqueue_CanExecuteNextOperationIfPreviousFailed()
        {
            var failedTask = _sut.Enqueue(_ => Task.FromException(new InvalidOperationException("Failure")));
            var nextTask = _sut.Enqueue(_ => Task.FromResult(42)).AsTask();

            Assert.That(nextTask.Result, Is.EqualTo(42));
            Assert.That(() => failedTask.IsFaulted, Is.True.After(100, 10));
        }

        [Test]
        public void Enqueue_CanExecuteNextOperationIfPreviousCanceled()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var canceledTask = _sut.Enqueue(Task.FromCanceled, cts.Token);
            var nextTask = _sut.Enqueue(_ => Task.FromResult(42)).AsTask();

            Assert.That(nextTask.Result, Is.EqualTo(42));
            Assert.That(canceledTask.IsCanceled, Is.True);
        }
    }
}