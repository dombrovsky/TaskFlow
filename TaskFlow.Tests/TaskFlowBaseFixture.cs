namespace TaskFlow.Tests
{
    using NUnit.Framework;
    using System.Threading.Tasks.Flow;

    [TestFixture]
    public abstract class TaskFlowBaseFixture<T> : TaskSchedulerBaseFixture<T>
        where T : ITaskFlow
    {
        [Test]
        [Timeout(1000)]
        public async Task DisposeAsync_ShouldCancelPendingTask()
        {
            var sut = CreateSut();

            var task1 = sut.Enqueue(token => Task.Delay(1000, token)).AsTask();
            var task2 = sut.Enqueue(token => Task.Delay(1000, token)).AsTask();
            var task3 = sut.Enqueue(token => Task.Delay(1000, token)).AsTask();
            Assert.That(task1.IsCompleted && task2.IsCompleted && task3.IsCompleted, Is.False);

            await sut.DisposeAsync().ConfigureAwait(false);
            Assert.That(task1.IsCanceled, Is.True);
            Assert.That(task2.IsCanceled, Is.True);
            Assert.That(task3.IsCanceled, Is.True);
        }

        [Test]
        [Timeout(1000)]
        public async Task DisposeAsync_ShouldWaitQueuedOperations()
        {
            var sut = CreateSut();

            var task1 = sut.Enqueue(() => Thread.Sleep(50)).AsTask();
            var task2 = sut.Enqueue(() => Thread.Sleep(50)).AsTask();
            var task3 = sut.Enqueue(() => Thread.Sleep(50)).AsTask();
            Assert.That(task1.IsCompleted && task2.IsCompleted && task3.IsCompleted, Is.False);

            await sut.DisposeAsync().ConfigureAwait(false);

            Assert.That(task1.IsCompleted, Is.True);
            Assert.That(task2.IsCompleted, Is.True);
            Assert.That(task3.IsCompleted, Is.True);
        }

        [Test]
        [Timeout(1000)]
        public void Dispose_ShouldWaitQueuedOperations()
        {
            var sut = CreateSut();

            var task1 = sut.Enqueue(() => Thread.Sleep(50)).AsTask();
            var task2 = sut.Enqueue(() => Thread.Sleep(50)).AsTask();
            var task3 = sut.Enqueue(() => Thread.Sleep(50)).AsTask();
            Assert.That(task1.IsCompleted && task2.IsCompleted && task3.IsCompleted, Is.False);

            sut.Dispose();

            Assert.That(task1.IsCompleted, Is.True);
            Assert.That(task2.IsCompleted, Is.True);
            Assert.That(task3.IsCompleted, Is.True);
        }

        [Test]
        [Timeout(1000)]
        public void Dispose_ShouldReturnFalseIfTimedOut()
        {
            var sut = CreateSut();

            var task = sut.Enqueue(() => Thread.Sleep(1000)).AsTask();
            Assert.That(task.IsCompleted, Is.False);

            var disposed = sut.Dispose(TimeSpan.FromMilliseconds(100));

            Assert.That(disposed, Is.False);
        }

        [Test]
        [Timeout(1000)]
        public void Dispose_ShouldReturnTrueIfNotTimedOut()
        {
            var sut = CreateSut();

            var task = sut.Enqueue(() => Thread.Sleep(50)).AsTask();
            Assert.That(task.IsCompleted, Is.False);

            var disposed = sut.Dispose(TimeSpan.FromMilliseconds(100));

            Assert.That(disposed, Is.True);
        }

        [Test]
        [Timeout(1000)]
        public void Dispose_CanCallMultipleTimes()
        {
            var sut = CreateSut();
            sut.Enqueue(() => Thread.Sleep(50)).AsTask();

            sut.Dispose();

            Assert.That(sut.Dispose, Throws.Nothing);
            Assert.That(sut.Dispose, Throws.Nothing);
        }

        [Test]
        [Timeout(1000)]
        public async Task DisposeAsync_CanCallMultipleTimes()
        {
            var sut = CreateSut();
            _ = sut.Enqueue(() => Thread.Sleep(50)).AsTask();

            await sut.DisposeAsync().ConfigureAwait(false);

            Assert.That(sut.DisposeAsync, Throws.Nothing);
            Assert.That(sut.DisposeAsync, Throws.Nothing);
        }

        [Test]
        [Timeout(1000)]
        public async Task Dispose_CanCallAfterDisposeAsync()
        {
            var sut = CreateSut();
            _ = sut.Enqueue(() => Thread.Sleep(50)).AsTask();

            await sut.DisposeAsync().ConfigureAwait(false);

            Assert.That(sut.Dispose, Throws.Nothing);
        }

        [Test]
        public void Enqueue_ShouldThrowAfterDispose()
        {
            var sut = CreateSut();
            sut.Dispose();

            Assert.That(() => sut.Enqueue(() => Task.CompletedTask), Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public async Task Enqueue_ShouldThrowAfterDisposeAsync()
        {
            var sut = CreateSut();
            await sut.DisposeAsync().ConfigureAwait(false);

            Assert.That(() => sut.Enqueue(() => Task.CompletedTask), Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void Dispose_ShouldNotThrowIfTaskFuncFailed()
        {
            var sut = CreateSut();

            var failedTask = sut.Enqueue(_ => ValueTask.FromException(new InvalidOperationException("Failure"))).AsTask();

            Assert.That(sut.Dispose, Throws.Nothing);
            Assert.That(failedTask.IsFaulted, Is.True);
        }

        [Test]
        public void DisposeAsync_ShouldNotThrowIfTaskFuncFailed()
        {
            var sut = CreateSut();

            var failedTask = sut.Enqueue(_ => Task.FromException(new InvalidOperationException("Failure"))).AsTask();

            Assert.That(sut.DisposeAsync, Throws.Nothing);
            Assert.That(failedTask.IsFaulted, Is.True);
        }
    }
}