namespace TaskFlow.Tests.Extensions
{
    using NUnit.Framework;
    using System.Collections.Concurrent;
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks.Flow;

    [TestFixture]
    internal sealed class FuturePolicyMiddlewareSeamsFixture
    {
        [SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "NUnit requires the parameter type of this public parameterized test to be publicly accessible.")]
        public enum OwnershipCancellation
        {
            LocalScope,
            SharedLane,
        }

        [Test]
        public async Task Retry_UsesOneQueueTurnAndInvokesDownstreamOncePerAttempt()
        {
            var terminal = new CountingInlineTerminal();
            var downstream = new ExecutionCounterMiddleware();
            var completion = new CompletionCounterMiddleware();
            var attempts = 0;
            var scheduler = terminal
                .UseMiddleware(new RetryMiddleware(3))
                .UseMiddleware(downstream)
                .UseMiddleware(completion);

            var result = await scheduler.Enqueue(() =>
            {
                attempts++;
                if (attempts < 3) throw new InvalidOperationException("retryable");
                return 42;
            });

            Assert.That(result, Is.EqualTo(42));
            Assert.That(terminal.EnqueueCount, Is.EqualTo(1));
            Assert.That(attempts, Is.EqualTo(3));
            Assert.That(downstream.CallCount, Is.EqualTo(3));
            Assert.That(completion.CallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task SingleFlight_SharesOneProducerAndAllowsIndependentWaitCancellation()
        {
            var terminal = new CountingInlineTerminal();
            var middleware = new SingleFlightIntMiddleware();
            var execution = new ExecutionCounterMiddleware();
            var completion = new CompletionCounterMiddleware();
            var scheduler = terminal
                .UseMiddleware(middleware)
                .UseMiddleware(execution)
                .UseMiddleware(completion);
            var producerGate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var firstWait = new CancellationTokenSource();

            var first = scheduler.Enqueue<int>((_, _) => new ValueTask<int>(producerGate.Task), "key", firstWait.Token);
            await middleware.ProducerStarted;
            var follower = scheduler.Enqueue<int>((_, _) => throw new AssertionException("A follower must not create another producer."), "key", CancellationToken.None);

            await firstWait.CancelAsync();
            Assert.That(async () => await first, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(follower.IsCompleted, Is.False);

            producerGate.SetResult(42);
            Assert.That(await follower, Is.EqualTo(42));
            Assert.That(terminal.EnqueueCount, Is.EqualTo(1));
            Assert.That(execution.CallCount, Is.EqualTo(1));
            Assert.That(completion.CallCount, Is.EqualTo(1));
            Assert.That(middleware.ActiveCount, Is.Zero);
        }

        [Test]
        public async Task DuplicateSuppression_RejectsConcurrentDuplicateAndReleasesKeyAfterCompletion()
        {
            var terminal = new CountingInlineTerminal();
            var middleware = new DuplicateSuppressionMiddleware();
            var execution = new ExecutionCounterMiddleware();
            var scheduler = terminal
                .UseMiddleware(middleware)
                .UseMiddleware(execution);
            var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            var accepted = scheduler.Enqueue<int>((_, _) => new ValueTask<int>(gate.Task), "key", CancellationToken.None);
            await middleware.Accepted;
            var duplicate = scheduler.Enqueue<int>((_, _) => new ValueTask<int>(99), "key", CancellationToken.None);

            Assert.That(async () => await duplicate, Throws.TypeOf<InvalidOperationException>());
            Assert.That(terminal.EnqueueCount, Is.EqualTo(1));
            Assert.That(execution.CallCount, Is.EqualTo(1));

            gate.SetResult(42);
            Assert.That(await accepted, Is.EqualTo(42));
            Assert.That(middleware.ActiveCount, Is.Zero);

            Assert.That(await scheduler.Enqueue<int>((_, _) => new ValueTask<int>(7), "key", CancellationToken.None), Is.EqualTo(7));
            Assert.That(terminal.EnqueueCount, Is.EqualTo(2));
            Assert.That(execution.CallCount, Is.EqualTo(2));
        }

        [Test]
        public async Task KeyedCoordinatorAdapter_EvictsInactiveKeysAndDoesNotTransferOwnership()
        {
            var coordinator = new TestKeyedCoordinator();
            var terminal = new CountingInlineTerminal();
            var scheduler = terminal.UseMiddleware(new KeyedCoordinatorMiddleware(coordinator));
            var firstGate = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = 0;

            var first = scheduler.Enqueue<int>(async (_, _) =>
            {
                Interlocked.Increment(ref entered);
                await firstGate.Task;
                return 1;
            }, "same", CancellationToken.None);
            await coordinator.FirstEntered;
            var second = scheduler.Enqueue<int>((_, _) =>
            {
                Interlocked.Increment(ref entered);
                return new ValueTask<int>(2);
            }, "same", CancellationToken.None);
            var differentKey = scheduler.Enqueue<int>((_, _) => new ValueTask<int>(3), "other", CancellationToken.None);

            await Task.Yield();
            Assert.That(Volatile.Read(ref entered), Is.EqualTo(1));
            Assert.That(await differentKey, Is.EqualTo(3), "A different key should not wait for the occupied lane.");
            firstGate.SetResult(null);
            var results = await Task.WhenAll(first, second);
            Assert.That(results[0], Is.EqualTo(1));
            Assert.That(results[1], Is.EqualTo(2));
            Assert.That(coordinator.MaximumSameKeyConcurrency, Is.EqualTo(1));
            Assert.That(coordinator.ActiveKeyCount, Is.Zero);
            Assert.That(coordinator.IsDisposed, Is.False);
            Assert.That(terminal.EnqueueCount, Is.EqualTo(3));

            coordinator.Dispose();
            Assert.That(coordinator.IsDisposed, Is.True);
        }

        [Test]
        public async Task LocalScope_TracksSubmissionBeforeMiddlewareAdmissionOnSharedLane()
        {
            var lane = new CountingInlineTerminal();
            var admission = new BlockingAdmissionMiddleware();
            var decoratedLane = lane.UseMiddleware(admission);
            var firstScope = new LocalOperationScope(decoratedLane);
            var secondScope = new LocalOperationScope(decoratedLane);

            var operation = firstScope.Enqueue<int>((_, _) => new ValueTask<int>(42), null, CancellationToken.None);
            await admission.Entered;

            Assert.That(firstScope.ActiveCount, Is.EqualTo(1));
            Assert.That(secondScope.ActiveCount, Is.Zero);
            Assert.That(lane.EnqueueCount, Is.Zero);

            admission.Release();
            Assert.That(await operation, Is.EqualTo(42));
            Assert.That(firstScope.ActiveCount, Is.Zero);
            Assert.That(lane.EnqueueCount, Is.EqualTo(1));
        }

        [TestCase(OwnershipCancellation.LocalScope)]
        [TestCase(OwnershipCancellation.SharedLane)]
        public async Task SharedProducer_DetachesCallerButPreservesOwnershipCancellation(OwnershipCancellation cancellation)
        {
            using var localOwner = new CancellationTokenSource();
            using var sharedLane = new CancellationTokenSource();
            using var caller = new CancellationTokenSource();
            var terminal = new CountingInlineTerminal();
            var ownership = new OwnershipSingleFlightMiddleware(localOwner.Token, sharedLane.Token);
            var scheduler = terminal
                .UseMiddleware(ownership)
                .UseMiddleware(new DropProducerTokenMiddleware());
            var producerEntered = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var first = scheduler.Enqueue<int>(async (_, producerToken) =>
            {
                producerEntered.TrySetResult(null);
                await Task.Delay(Timeout.InfiniteTimeSpan, producerToken);
                return 42;
            }, "key", caller.Token);
            await producerEntered.Task;
            var follower = scheduler.Enqueue<int>((_, _) => throw new AssertionException("A follower must not invoke the producer."), "key", CancellationToken.None);

            await caller.CancelAsync();
            Assert.That(async () => await first, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(follower.IsCompleted, Is.False, "Canceling one caller must not cancel shared producer work.");

            if (cancellation == OwnershipCancellation.LocalScope)
            {
                await localOwner.CancelAsync();
            }
            else
            {
                await sharedLane.CancelAsync();
            }

            Assert.That(async () => await follower, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(terminal.EnqueueCount, Is.EqualTo(1));
            Assert.That(() => ownership.ActiveCount, Is.Zero.After(100, 10));
        }

        private sealed class RetryMiddleware : ITaskSchedulerExecutionMiddleware
        {
            private readonly int _maximumAttempts;
            public RetryMiddleware(int maximumAttempts) => _maximumAttempts = maximumAttempts;

            public async ValueTask<TResult> InvokeAsync<TResult>(TaskSchedulerOperationContext context, TaskSchedulerExecutionDelegate<TResult> continuation)
            {
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        return await continuation(context);
                    }
                    catch (InvalidOperationException) when (attempt < _maximumAttempts)
                    {
                    }
                }
            }
        }

        private sealed class SingleFlightIntMiddleware : ITaskSchedulerEnqueueMiddleware
        {
            private readonly ConcurrentDictionary<string, Producer> _producers = new ConcurrentDictionary<string, Producer>();
            private readonly TaskCompletionSource<object?> _producerStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task ProducerStarted => _producerStarted.Task;
            public int ActiveCount => _producers.Count;

            public Task<TResult> InvokeAsync<TResult>(TaskSchedulerEnqueueContext<TResult> context, TaskSchedulerEnqueueDelegate<TResult> continuation)
            {
                if (typeof(TResult) != typeof(int)) throw new NotSupportedException("This test prototype supports Int32 results only.");
                var key = (string)context.State!;
                var candidate = new Producer();
                var producer = _producers.GetOrAdd(key, candidate);
                if (ReferenceEquals(candidate, producer))
                {
                    _ = Produce(key, producer, context, continuation);
                }

                return Wait<TResult>(producer.Task, context.CallerCancellationToken);
            }

            [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The single-flight prototype must transfer every producer failure to all follower tasks.")]
            private async Task Produce<TResult>(string key, Producer producer, TaskSchedulerEnqueueContext<TResult> context, TaskSchedulerEnqueueDelegate<TResult> continuation)
            {
                _producerStarted.TrySetResult(null);
                try
                {
                    var result = await continuation(context.WithCancellationToken(CancellationToken.None));
                    producer.TrySetResult((int)(object)result!);
                }
                catch (Exception exception)
                {
                    producer.TrySetException(exception);
                }
                finally
                {
                    _producers.TryRemove(new KeyValuePair<string, Producer>(key, producer));
                }
            }

            private static async Task<TResult> Wait<TResult>(Task<int> producer, CancellationToken cancellationToken)
                => (TResult)(object)await producer.WaitAsync(cancellationToken);

            private sealed class Producer
            {
                private readonly TaskCompletionSource<int> _completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                public Task<int> Task => _completion.Task;
                public void TrySetResult(int result) => _completion.TrySetResult(result);
                public void TrySetException(Exception exception) => _completion.TrySetException(exception);
            }
        }

        private sealed class DuplicateSuppressionMiddleware : ITaskSchedulerEnqueueMiddleware
        {
            private readonly ConcurrentDictionary<string, object?> _active = new ConcurrentDictionary<string, object?>();
            private readonly TaskCompletionSource<object?> _accepted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            public Task Accepted => _accepted.Task;
            public int ActiveCount => _active.Count;

            public async Task<TResult> InvokeAsync<TResult>(TaskSchedulerEnqueueContext<TResult> context, TaskSchedulerEnqueueDelegate<TResult> continuation)
            {
                var key = (string)context.State!;
                if (!_active.TryAdd(key, null)) throw new InvalidOperationException("An equivalent operation is already active.");
                _accepted.TrySetResult(null);
                try
                {
                    return await continuation(context);
                }
                finally
                {
                    _active.TryRemove(key, out _);
                }
            }
        }

        private sealed class KeyedCoordinatorMiddleware : ITaskSchedulerEnqueueMiddleware
        {
            private readonly TestKeyedCoordinator _coordinator;
            public KeyedCoordinatorMiddleware(TestKeyedCoordinator coordinator) => _coordinator = coordinator;
            public Task<TResult> InvokeAsync<TResult>(TaskSchedulerEnqueueContext<TResult> context, TaskSchedulerEnqueueDelegate<TResult> continuation)
                => _coordinator.Run((string)context.State!, () => continuation(context));
        }

        private sealed class TestKeyedCoordinator : IDisposable
        {
            private readonly object _sync = new object();
            private readonly Dictionary<string, Lane> _lanes = new Dictionary<string, Lane>();
            private readonly TaskCompletionSource<object?> _firstEntered = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _maximumSameKeyConcurrency;
            private bool _isDisposed;

            public Task FirstEntered => _firstEntered.Task;
            public int ActiveKeyCount
            {
                get
                {
                    lock (_sync) return _lanes.Count;
                }
            }
            public int MaximumSameKeyConcurrency => _maximumSameKeyConcurrency;
            public bool IsDisposed => _isDisposed;

            public async Task<TResult> Run<TResult>(string key, Func<Task<TResult>> operation)
            {
                Lane lane;
                lock (_sync)
                {
                    ObjectDisposedException.ThrowIf(_isDisposed, this);
                    if (!_lanes.TryGetValue(key, out lane!))
                    {
                        lane = new Lane();
                        _lanes.Add(key, lane);
                    }

                    lane.Users++;
                }

                await lane.Gate.WaitAsync();
                var concurrency = Interlocked.Increment(ref lane.Executing);
                UpdateMaximum(concurrency);
                _firstEntered.TrySetResult(null);
                try
                {
                    return await operation();
                }
                finally
                {
                    Interlocked.Decrement(ref lane.Executing);
                    lane.Gate.Release();
                    lock (_sync)
                    {
                        lane.Users--;
                        if (lane.Users == 0 && _lanes.TryGetValue(key, out var current) && ReferenceEquals(current, lane))
                        {
                            _lanes.Remove(key);
                        }
                    }
                }
            }

            public void Dispose()
            {
                lock (_sync) _isDisposed = true;
            }

            private void UpdateMaximum(int value)
            {
                int current;
                do
                {
                    current = _maximumSameKeyConcurrency;
                    if (current >= value) return;
                }
                while (Interlocked.CompareExchange(ref _maximumSameKeyConcurrency, value, current) != current);
            }

            private sealed class Lane
            {
                public SemaphoreSlim Gate { get; } = new SemaphoreSlim(1, 1);
                public int Users;
                public int Executing;
            }
        }

        private sealed class LocalOperationScope : ITaskScheduler
        {
            private readonly ITaskScheduler _sharedLane;
            private int _activeCount;
            public LocalOperationScope(ITaskScheduler sharedLane) => _sharedLane = sharedLane;
            public int ActiveCount => Volatile.Read(ref _activeCount);

            public async Task<TResult> Enqueue<TResult>(Func<object?, CancellationToken, ValueTask<TResult>> taskFunc, object? state, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _activeCount);
                try
                {
                    return await _sharedLane.Enqueue(taskFunc, state, cancellationToken);
                }
                finally
                {
                    Interlocked.Decrement(ref _activeCount);
                }
            }
        }

        private sealed class DropProducerTokenMiddleware : ITaskSchedulerEnqueueMiddleware
        {
            public Task<TResult> InvokeAsync<TResult>(TaskSchedulerEnqueueContext<TResult> context, TaskSchedulerEnqueueDelegate<TResult> continuation)
                => continuation(context.WithCancellationToken(CancellationToken.None));
        }

        private sealed class OwnershipSingleFlightMiddleware : ITaskSchedulerEnqueueMiddleware
        {
            private readonly CancellationToken _localOwnerToken;
            private readonly CancellationToken _sharedLaneToken;
            private readonly ConcurrentDictionary<string, OwnedProducer> _producers = new ConcurrentDictionary<string, OwnedProducer>();

            public OwnershipSingleFlightMiddleware(CancellationToken localOwnerToken, CancellationToken sharedLaneToken)
            {
                _localOwnerToken = localOwnerToken;
                _sharedLaneToken = sharedLaneToken;
            }

            public int ActiveCount => _producers.Count;

            public Task<TResult> InvokeAsync<TResult>(TaskSchedulerEnqueueContext<TResult> context, TaskSchedulerEnqueueDelegate<TResult> continuation)
            {
                if (typeof(TResult) != typeof(int)) throw new NotSupportedException("This test prototype supports Int32 results only.");
                var key = (string)context.State!;
                var candidate = new OwnedProducer();
                var producer = _producers.GetOrAdd(key, candidate);
                if (ReferenceEquals(candidate, producer))
                {
                    _ = Produce(key, producer, context, continuation);
                }

                return Wait<TResult>(producer.Task, context.CallerCancellationToken, _localOwnerToken, _sharedLaneToken);
            }

            [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The ownership prototype must transfer every producer failure to all follower tasks.")]
            private async Task Produce<TResult>(string key, OwnedProducer producer, TaskSchedulerEnqueueContext<TResult> context, TaskSchedulerEnqueueDelegate<TResult> continuation)
            {
                using var ownership = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, _localOwnerToken, _sharedLaneToken);
                try
                {
                    var result = await continuation(context.WithCancellationToken(ownership.Token));
                    producer.TrySetResult((int)(object)result!);
                }
                catch (Exception exception)
                {
                    producer.TrySetException(exception);
                }
                finally
                {
                    _producers.TryRemove(new KeyValuePair<string, OwnedProducer>(key, producer));
                }
            }

            private static async Task<TResult> Wait<TResult>(Task<int> producer, CancellationToken callerToken, CancellationToken localOwnerToken, CancellationToken sharedLaneToken)
            {
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(callerToken, localOwnerToken, sharedLaneToken);
                return (TResult)(object)await producer.WaitAsync(wait.Token);
            }

            private sealed class OwnedProducer
            {
                private readonly TaskCompletionSource<int> _completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                public Task<int> Task => _completion.Task;
                public void TrySetResult(int result) => _completion.TrySetResult(result);
                public void TrySetException(Exception exception) => _completion.TrySetException(exception);
            }
        }

        private sealed class BlockingAdmissionMiddleware : ITaskSchedulerEnqueueMiddleware
        {
            private readonly TaskCompletionSource<object?> _entered = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<object?> _release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            public Task Entered => _entered.Task;
            public void Release() => _release.TrySetResult(null);

            public async Task<TResult> InvokeAsync<TResult>(TaskSchedulerEnqueueContext<TResult> context, TaskSchedulerEnqueueDelegate<TResult> continuation)
            {
                _entered.TrySetResult(null);
                await _release.Task;
                return await continuation(context);
            }
        }

        private sealed class ExecutionCounterMiddleware : ITaskSchedulerExecutionMiddleware
        {
            public int CallCount { get; private set; }
            public ValueTask<TResult> InvokeAsync<TResult>(TaskSchedulerOperationContext context, TaskSchedulerExecutionDelegate<TResult> continuation)
            {
                CallCount++;
                return continuation(context);
            }
        }

        private sealed class CompletionCounterMiddleware : ITaskSchedulerCompletionMiddleware
        {
            public int CallCount { get; private set; }
            public ValueTask<TaskSchedulerOperationOutcome<TResult>> InvokeAsync<TResult>(TaskSchedulerOperationContext context, TaskSchedulerOperationOutcome<TResult> outcome, TaskSchedulerCompletionDelegate<TResult> continuation)
            {
                CallCount++;
                return continuation(context, outcome);
            }
        }

        private sealed class CountingInlineTerminal : ITaskScheduler
        {
            public int EnqueueCount { get; private set; }
            public async Task<TResult> Enqueue<TResult>(Func<object?, CancellationToken, ValueTask<TResult>> taskFunc, object? state, CancellationToken cancellationToken)
            {
                EnqueueCount++;
                return await taskFunc(state, cancellationToken);
            }
        }

    }
}
