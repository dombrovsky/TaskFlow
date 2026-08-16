namespace TaskFlow.Tests
{
    using NUnit.Framework;
    using System.Threading.Tasks;
    using System.Threading.Tasks.Flow;

    [TestFixture]
    internal abstract class TaskFlowBaseFixture<T> : TaskSchedulerBaseFixture<T>
        where T : ITaskFlow
    {
        [Test]
        [CancelAfter(1000)]
        public async Task DisposeAsync_ShouldCancelPendingTask()
        {
            var sut = CreateSut();

            var task1 = sut.Enqueue(token => Task.Delay(1000, token));
            var task2 = sut.Enqueue(token => Task.Delay(1000, token));
            var task3 = sut.Enqueue(token => Task.Delay(1000, token));
            Assert.That(task1.IsCompleted && task2.IsCompleted && task3.IsCompleted, Is.False);

            await sut.DisposeAsync().ConfigureAwait(false);
            Assert.That(() => task1.IsCanceled, Is.True.After(100, 10), task1.Status.ToString);
            Assert.That(() => task2.IsCanceled, Is.True.After(100, 10), task2.Status.ToString);
            Assert.That(() => task3.IsCanceled, Is.True.After(100, 10), task3.Status.ToString);
        }

        [Test]
        [CancelAfter(1000)]
        public async Task DisposeAsync_ShouldWaitQueuedOperations()
        {
            var sut = CreateSut();

            var task1 = sut.Enqueue(() => Thread.Sleep(50));
            var task2 = sut.Enqueue(() => Thread.Sleep(50));
            var task3 = sut.Enqueue(() => Thread.Sleep(50));
            Assert.That(task1.IsCompleted && task2.IsCompleted && task3.IsCompleted, Is.False);

            await sut.DisposeAsync().ConfigureAwait(false);

            Assert.That(task1.IsCompleted, Is.True, task1.Status.ToString);
            Assert.That(task2.IsCompleted, Is.True, task2.Status.ToString);
            Assert.That(task3.IsCompleted, Is.True, task3.Status.ToString);
        }

        [Test]
        [CancelAfter(1000)]
        public void Dispose_ShouldWaitQueuedOperations()
        {
            var sut = CreateSut();

            var task1 = sut.Enqueue(() => Thread.Sleep(50));
            var task2 = sut.Enqueue(() => Thread.Sleep(50));
            var task3 = sut.Enqueue(() => Thread.Sleep(50));
            Assert.That(task1.IsCompleted && task2.IsCompleted && task3.IsCompleted, Is.False);

            sut.Dispose();

            Assert.That(() => task1.IsCompleted, Is.True.After(100, 10), task1.Status.ToString);
            Assert.That(() => task2.IsCompleted, Is.True.After(100, 10), task2.Status.ToString);
            Assert.That(() => task3.IsCompleted, Is.True.After(100, 10), task3.Status.ToString);
        }

        [Test]
        [CancelAfter(1000)]
        public void Dispose_ShouldReturnFalseIfTimedOut()
        {
            var sut = CreateSut();

            var task = sut.Enqueue(() => Thread.Sleep(500));
            Assert.That(task.IsCompleted, Is.False);

            var disposed = sut.Dispose(TimeSpan.FromMilliseconds(100));

            Assert.That(disposed, Is.False);
        }

        [Test]
        [CancelAfter(1000)]
        public void Dispose_ShouldReturnTrueIfNotTimedOut()
        {
            var sut = CreateSut();

            var task = sut.Enqueue(() => Thread.Sleep(50));
            Assert.That(task.IsCompleted, Is.False);

            var disposed = sut.Dispose(TimeSpan.FromMilliseconds(100));

            Assert.That(disposed, Is.True);
        }

        [Test]
        [CancelAfter(1000)]
        public void Dispose_CanCallMultipleTimes()
        {
            var sut = CreateSut();
            _ = sut.Enqueue(() => Thread.Sleep(50));

            sut.Dispose();

            Assert.That(sut.Dispose, Throws.Nothing);
            Assert.That(sut.Dispose, Throws.Nothing);
        }

        [Test]
        [CancelAfter(1000)]
        public async Task DisposeAsync_CanCallMultipleTimes()
        {
            var sut = CreateSut();
            _ = sut.Enqueue(() => Thread.Sleep(50));

            await sut.DisposeAsync().ConfigureAwait(false);

            await sut.DisposeAsync().ConfigureAwait(false);
            await sut.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        [CancelAfter(1000)]
        public async Task Dispose_CanCallAfterDisposeAsync()
        {
            var sut = CreateSut();
            _ = sut.Enqueue(() => Thread.Sleep(50));

            await sut.DisposeAsync().ConfigureAwait(false);

            Assert.That(sut.Dispose, Throws.Nothing);
        }

        [Test]
        public async Task Enqueue_ShouldThrowAfterDispose()
        {
            var sut = CreateSut();
            sut.Dispose();

            await Assert.ThatAsync(
                    async () => await sut.Enqueue(() => Task.CompletedTask).ConfigureAwait(false),
                    Throws.TypeOf<ObjectDisposedException>())
                .ConfigureAwait(false);
        }

        [Test]
        public async Task Enqueue_ShouldThrowAfterDisposeAsync()
        {
            var sut = CreateSut();
            await sut.DisposeAsync().ConfigureAwait(false);

            await Assert.ThatAsync(
                    async () => await sut.Enqueue(() => Task.CompletedTask).ConfigureAwait(false),
                    Throws.TypeOf<ObjectDisposedException>())
                .ConfigureAwait(false);
        }

        [Test]
        public void Dispose_ShouldNotThrowIfTaskFuncFailed()
        {
            var sut = CreateSut();

            var failedTask = sut.Enqueue(_ => Task.FromException(new InvalidOperationException("Failure")));

            Assert.That(sut.Dispose, Throws.Nothing);
            Assert.That(() => failedTask.IsFaulted, Is.True.After(100 ,10), failedTask.Status.ToString);
        }

        [Test]
        public void DisposeAsync_ShouldNotThrowIfTaskFuncFailed()
        {
            var sut = CreateSut();

            var failedTask = sut.Enqueue(_ => Task.FromException(new InvalidOperationException("Failure")));

            Assert.That(sut.DisposeAsync, Throws.Nothing);
            Assert.That(() => failedTask.IsFaulted, Is.True.After(100, 10), failedTask.Status.ToString);
        }

        [Test]
        [CancelAfter(3000)]
        public async Task DisposeAsync_WhenEnqueueRacesWithDispose_ShouldCompleteWithoutDeadlock()
        {
            var sut = CreateSut();
            using var operationStarted = new ManualResetEventSlim();

            var blockingTask = sut.Enqueue(async token =>
            {
                operationStarted.Set();
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            });

            Assert.That(operationStarted.Wait(500), Is.True);

            var disposeTask = sut.DisposeAsync().AsTask();
            var racedEnqueueTask = Task.Run(
                async () =>
                {
                    try
                    {
                        await sut.Enqueue(() => 42).ConfigureAwait(false);
                        return "completed";
                    }
                    catch (ObjectDisposedException)
                    {
                        return "disposed";
                    }
                    catch (OperationCanceledException)
                    {
                        return "canceled";
                    }
                });

            await Assert.ThatAsync(async () => await disposeTask.ConfigureAwait(false), Throws.Nothing).ConfigureAwait(false);

            var raceResult = await racedEnqueueTask.ConfigureAwait(false);
            Assert.That(raceResult, Is.EqualTo("completed").Or.EqualTo("disposed").Or.EqualTo("canceled"));
            Assert.That(() => blockingTask.IsCanceled, Is.True.After(200, 10), blockingTask.Status.ToString);

            await Assert.ThatAsync(
                    async () => await sut.Enqueue(() => Task.CompletedTask).ConfigureAwait(false),
                    Throws.TypeOf<ObjectDisposedException>())
                .ConfigureAwait(false);
        }
    }
}
