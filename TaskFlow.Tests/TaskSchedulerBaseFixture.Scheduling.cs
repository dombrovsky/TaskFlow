namespace TaskFlow.Tests
{
    using System.Collections.Concurrent;
    using NUnit.Framework;
    using System.Threading.Tasks;
    using System.Threading.Tasks.Flow;

    internal abstract partial class TaskSchedulerBaseFixture<T>
    {
        [Test]
        public async Task Enqueue_ShouldExecuteInScheduledOrder()
        {
            var counter = 0;
            var tasks = Enumerable.Range(0, 10)
                .Select(i => _sut.Enqueue(() => Interlocked.Increment(ref counter) == i + 1)).ToArray();

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
                });

            Assert.That(completedEvent.Wait(0), Is.False);
            Assert.That(task.Wait(200), Is.True);
            Assert.That(completedEvent.Wait(0), Is.True);
        }

        [Test]
        public void Enqueue_ReturnedTaskShouldBeFailedWhenTaskFuncFailed()
        {
            var task = _sut.Enqueue(_ => Task.FromException(new InvalidOperationException("Failure")));

            Assert.That(async () => await task.ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.EqualTo("Failure"));
            Assert.That(task.IsFaulted, Is.True);
        }

        [Test]
        public async Task Enqueue_ReturnedTaskShouldBeCanceledWhenTaskFuncCanceled()
        {
            using var cts = new CancellationTokenSource();
            var task = _sut.Enqueue(token => Task.Delay(1000, token), cts.Token);

            await cts.CancelAsync().ConfigureAwait(false);

            await Assert.ThatAsync(async () => await task.ConfigureAwait(false), Throws.InstanceOf<OperationCanceledException>())
                .ConfigureAwait(false);
            Assert.That(task.IsCanceled, Is.True);
        }

        [Test]
        public void Enqueue_CanExecuteNextOperationIfPreviousFailed()
        {
            var failedTask = _sut.Enqueue(_ => Task.FromException(new InvalidOperationException("Failure")));
            var nextTask = _sut.Enqueue(_ => Task.FromResult(42));

            Assert.That(nextTask.Result, Is.EqualTo(42));
            Assert.That(() => failedTask.IsFaulted, Is.True.After(100, 10));
        }

        [Test]
        public void Enqueue_CanExecuteNextOperationIfPreviousCanceled()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var canceledTask = _sut.Enqueue(Task.FromCanceled, cts.Token);
            var nextTask = _sut.Enqueue(_ => Task.FromResult(42));

            Assert.That(nextTask.Result, Is.EqualTo(42));
            Assert.That(() => canceledTask.IsCanceled, Is.True.After(100, 10));
        }

        [Test]
        public void Enqueue_WhenInitiallyCanceled_ShouldExecuteOperation()
        {
            using var completedEvent = new ManualResetEventSlim();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var task = _sut.Enqueue(() => completedEvent.Set(), cts.Token);

            Assert.That(task.Wait(200), Is.True);
            Assert.That(completedEvent.Wait(0), Is.True);
        }

        [Test]
        public async Task Enqueue_ExecuteInOrderIfIntermediateCanceled()
        {
            using var taskACompletionEvent = new ManualResetEventSlim();

            using var cts = new CancellationTokenSource();
            await cts.CancelAsync().ConfigureAwait(false);

            var counter = 0;
            var taskA = _sut.Enqueue(() =>
            {
                taskACompletionEvent.Wait();
                return Interlocked.Increment(ref counter) == 1;
            });
            var taskB = _sut.Enqueue(Task.FromCanceled, cts.Token);
            var taskC = _sut.Enqueue(() => Interlocked.Increment(ref counter) == 2);

            await Task.Delay(100).ConfigureAwait(false);
            taskACompletionEvent.Set();

            Assert.That(() => taskA.IsCompletedSuccessfully, Is.True.After(100, 10));
            Assert.That(await taskA.ConfigureAwait(false), Is.True);

            Assert.That(() => taskB.IsCanceled, Is.True.After(100, 10));

            Assert.That(() => taskC.IsCompletedSuccessfully, Is.True.After(100, 10));
            Assert.That(await taskC.ConfigureAwait(false), Is.True);
        }

        [Test]
        public void Enqueue_PropagatesStateToTaskFunc()
        {
            var task1 = _sut.Enqueue((state, _) => Task.FromResult(state), 42, CancellationToken.None);
            var task2 = _sut.Enqueue((state, _) => Task.FromResult(state), 24, CancellationToken.None);

            Assert.That(task1.Result, Is.EqualTo(42));
            Assert.That(task2.Result, Is.EqualTo(24));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task Enqueue_AsyncContinuationShouldHappenOnSameScheduler(bool captureContext)
        {
            var result = await _sut.Enqueue(async () =>
            {
                var synchronizationContext = SynchronizationContext.Current;
                await Task.Delay(100).ConfigureAwait(captureContext);
                var a = DateTime.Now;
                await Task.Delay(100).ConfigureAwait(captureContext);
                return (synchronizationContext, SynchronizationContext.Current);
            }).ConfigureAwait(false);

            Assert.That(result.Current, Is.EqualTo(captureContext ? result.synchronizationContext : null));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task Enqueue_AsyncContinuationShouldHappenBeforeNextEnqueuedItems(bool captureContext)
        {
            var counter = 0;
            var task1 = _sut.Enqueue(async () =>
            {
                await Task.Delay(100).ConfigureAwait(captureContext);
                Interlocked.Increment(ref counter);
                await Task.Delay(100).ConfigureAwait(captureContext);
                return Interlocked.Increment(ref counter);
            });
            var task2 = _sut.Enqueue(() => Interlocked.Increment(ref counter));

            await Task.WhenAll(task1, task2).ConfigureAwait(false);

            Assert.That(await task1.ConfigureAwait(false), Is.EqualTo(2));
            Assert.That(await task2.ConfigureAwait(false), Is.EqualTo(3));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task Enqueue_AsyncContinuationThrowsException_ShouldExecuteNextOperation(bool captureContext)
        {
            var counter = 0;
            var task1 = _sut.Enqueue(async () =>
            {
                await Task.Delay(10).ConfigureAwait(captureContext);
                if (Interlocked.Increment(ref counter) > 0)
                {
                    throw new InvalidOperationException("Failure");
                }

                return Interlocked.Increment(ref counter);
            });
            var task2 = _sut.Enqueue(() => Interlocked.Increment(ref counter));

            await task2.ConfigureAwait(false);

            Assert.That(() => task1.IsFaulted, Is.True.After(100, 10));
            Assert.That(task1.Exception?.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(await task2.ConfigureAwait(false), Is.EqualTo(2));
        }

        [Test]
        public async Task Enqueue_FromMultipleProducers_ShouldExecuteAllOperationsOnceAndKeepPerProducerOrder()
        {
            const int producerCount = 8;
            const int operationsPerProducer = 40;

            using var startGate = new ManualResetEventSlim();
            var producerTasks = new Task[producerCount];
            var scheduledTasks = new ConcurrentBag<Task>();
            var executionOrder = new List<(int Producer, int Index)>();
            var executionLock = new object();

            for (var producer = 0; producer < producerCount; producer++)
            {
                var producerId = producer;
                producerTasks[producer] = Task.Run(
                    () =>
                    {
                        startGate.Wait();
                        for (var index = 0; index < operationsPerProducer; index++)
                        {
                            var operationIndex = index;
                            var task = _sut.Enqueue(
                                () =>
                                {
                                    lock (executionLock)
                                    {
                                        executionOrder.Add((producerId, operationIndex));
                                    }
                                });
                            scheduledTasks.Add(task);
                        }
                    });
            }

            startGate.Set();
            await Task.WhenAll(producerTasks).ConfigureAwait(false);
            await Task.WhenAll(scheduledTasks).ConfigureAwait(false);

            Assert.That(executionOrder.Count, Is.EqualTo(producerCount * operationsPerProducer));

            var groupedByProducer = executionOrder
                .GroupBy(item => item.Producer)
                .ToDictionary(group => group.Key, group => group.Select(item => item.Index).ToArray());

            Assert.That(groupedByProducer.Count, Is.EqualTo(producerCount));
            for (var producer = 0; producer < producerCount; producer++)
            {
                Assert.That(groupedByProducer.ContainsKey(producer), Is.True);
                Assert.That(groupedByProducer[producer], Is.EqualTo(Enumerable.Range(0, operationsPerProducer).ToArray()));
            }
        }
    }
}
