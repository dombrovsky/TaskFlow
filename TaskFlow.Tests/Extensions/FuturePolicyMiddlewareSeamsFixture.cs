namespace TaskFlow.Tests.Extensions;

using NUnit.Framework;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks.Flow;

[TestFixture]
[CancelAfter(10000)]
[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Factories transfer middleware ownership to the explicit lifetime.")]
internal sealed class FuturePolicyMiddlewareSeamsFixture
{
    private static readonly string[] ProducerCleanupOrder = ["producer:terminated", "key:released", "cleanup"];

    private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

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
            if (attempts < 3)
                throw new InvalidOperationException("retryable");
            return 42;
        });

        Assert.That(result, Is.EqualTo(42));
        Assert.That(terminal.EnqueueCount, Is.EqualTo(1));
        Assert.That(attempts, Is.EqualTo(3));
        Assert.That(downstream.CallCount, Is.EqualTo(3));
        Assert.That(completion.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task SingleFlight_FollowerShortCircuitsWhileLeaderAwaitsProducer()
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

        Assert.That(first.IsCompleted, Is.False);
        Assert.That(follower.IsCompleted, Is.False);

        producerGate.SetResult(42);
        Assert.That(await first, Is.EqualTo(42));
        Assert.That(await follower, Is.EqualTo(42));
        Assert.That(terminal.EnqueueCount, Is.EqualTo(1));
        Assert.That(execution.CallCount, Is.EqualTo(1));
        Assert.That(completion.CallCount, Is.EqualTo(1));
        Assert.That(middleware.ActiveCount, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SingleFlight_SharesOneProducerAndAllowsIndependentWaitCancellation(bool producerFails)
    {
        await using var lifetime = new TaskSchedulerMiddlewareLifetime();
        var terminal = new CountingInlineTerminal();
        var events = new ConcurrentQueue<string>();
        var middleware = new OwnedSingleFlight(events, default, lifetime.StoppingToken);
        var execution = new ExecutionCounterMiddleware();
        var completion = new CompletionCounterMiddleware();
        var scheduler = terminal.UseMiddleware(new OwnedFactory(middleware), lifetime)
            .UseMiddleware(execution).UseMiddleware(completion);
        using var leaderWait = new CancellationTokenSource();
        using var followerWait = new CancellationTokenSource();
        var entered = Signal();
        var release = Signal();
        var failure = new FormatException("shared producer");
        var first = scheduler.Enqueue<int>(async (_, token) =>
        {
            entered.TrySetResult(true);
            await release.Task;
            token.ThrowIfCancellationRequested();
            if (producerFails)
                throw failure;
            return 42;
        }, "key", leaderWait.Token);
        try
        {
            await entered.Task;
            var canceledFollower = scheduler.Enqueue<int>((_, _) => throw new AssertionException("Follower executed."), "key", followerWait.Token);
            var survivor = scheduler.Enqueue<int>((_, _) => throw new AssertionException("Follower executed."), "key", default);
            await leaderWait.CancelAsync();
            await followerWait.CancelAsync();
            await Assert.ThatAsync(async () => await first, Throws.InstanceOf<OperationCanceledException>());
            await Assert.ThatAsync(async () => await canceledFollower, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(survivor.IsCompleted, Is.False);
            Assert.That(middleware.ActiveCount, Is.EqualTo(1));
            Assert.That(middleware.Released, Is.Zero);
            Assert.That(middleware.Cleanups, Is.Zero);
            var lateFollower = scheduler.Enqueue<int>((_, _) => throw new AssertionException("Canceled caller retired the active key."), "key", default);
            Assert.That(terminal.EnqueueCount, Is.EqualTo(1));
            release.SetResult(true);
            if (producerFails)
            {
                await Assert.ThatAsync(async () => await survivor, Throws.Exception.SameAs(failure));
                await Assert.ThatAsync(async () => await lateFollower, Throws.Exception.SameAs(failure));
            }
            else
            {
                Assert.That(await survivor, Is.EqualTo(42));
                Assert.That(await lateFollower, Is.EqualTo(42));
            }
            Assert.That(await middleware.ProducerObserved, producerFails ? Is.SameAs(failure) : Is.Null);
            Assert.That(middleware.ActiveCount, Is.Zero);
            Assert.That(middleware.Released, Is.EqualTo(1));
            Assert.That(execution.CallCount, Is.EqualTo(1));
            // Each canceled/faulted caller completes its own operation; followers never execute.
            Assert.That(completion.CallCount, Is.EqualTo(producerFails ? 4 : 2));
            Assert.That(await scheduler.Enqueue<int>((_, _) => new ValueTask<int>(7), "key", default), Is.EqualTo(7));
            Assert.That(terminal.EnqueueCount, Is.EqualTo(2));
            Assert.That(middleware.ActiveCount, Is.Zero);
            await lifetime.DisposeAsync();
            Assert.That(middleware.Released, Is.EqualTo(2));
            Assert.That(middleware.Cleanups, Is.EqualTo(1));
        }
        finally { release.TrySetResult(true); await lifetime.DisposeAsync(); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SingleFlight_PublishesSharedCompletionBeforeRetiringKey(bool producerFails)
    {
        await using var lifetime = new TaskSchedulerMiddlewareLifetime();
        var terminal = new CountingInlineTerminal();
        var events = new ConcurrentQueue<string>();
        var failure = new FormatException("shared producer");
        Task<int>? joining = null;
        ITaskScheduler? scheduler = null;
        OwnedSingleFlight? middleware = null;
        middleware = new OwnedSingleFlight(events, default, default, () =>
        {
            Assert.That(middleware!.ActiveCount, Is.EqualTo(1));
            Assert.That(middleware.Released, Is.Zero);
            // Force a concurrent submission after producer-scope cleanup and publication,
            // but before key retirement. It must still join the published producer.
            Task.Run(() =>
            {
                joining = scheduler!.Enqueue<int>((_, _) => throw new AssertionException("Successor started before key retirement."), "key", default);
            }).GetAwaiter().GetResult();
            Assert.That(joining!.IsCompleted, Is.True, "The shared outcome must already be published.");
            Assert.That(terminal.EnqueueCount, Is.EqualTo(1));
        });
        scheduler = terminal.UseMiddleware(new OwnedFactory(middleware), lifetime);
        var entered = Signal();
        var release = Signal();
        var first = scheduler.Enqueue<int>(async (_, _) =>
        {
            entered.TrySetResult(true);
            await release.Task;
            if (producerFails)
                throw failure;
            return 42;
        }, "key", default);
        try
        {
            await entered.Task;
            release.SetResult(true);
            Assert.That(await middleware.ProducerObserved, producerFails ? Is.SameAs(failure) : Is.Null);
            Assert.That(joining, Is.Not.Null);
            if (producerFails)
            {
                await Assert.ThatAsync(async () => await first, Throws.Exception.SameAs(failure));
                await Assert.ThatAsync(async () => await joining!, Throws.Exception.SameAs(failure));
            }
            else
            {
                Assert.That(await first, Is.EqualTo(42));
                Assert.That(await joining!, Is.EqualTo(42));
            }
            Assert.That(middleware.ActiveCount, Is.Zero);
            Assert.That(middleware.Released, Is.EqualTo(1));
            await lifetime.DisposeAsync();
            Assert.That(middleware.Cleanups, Is.EqualTo(1));
        }
        finally { release.TrySetResult(true); await lifetime.DisposeAsync(); }
    }

    [TestCase(OwnershipCancellation.LocalScope)]
    [TestCase(OwnershipCancellation.SharedLane)]
    public async Task SharedProducer_DetachesCallerButPreservesOwnershipCancellation(OwnershipCancellation cancellation)
    {
        await using var lifetime = new TaskSchedulerMiddlewareLifetime();
        using var localOwner = new CancellationTokenSource();
        using var sharedLane = new CancellationTokenSource();
        using var caller = new CancellationTokenSource();
        var events = new ConcurrentQueue<string>();
        var ownership = new OwnedSingleFlight(events, localOwner.Token, sharedLane.Token);
        var terminal = new CountingInlineTerminal();
        var scheduler = terminal.UseMiddleware(new OwnedFactory(ownership), lifetime)
            .UseMiddleware(new DropProducerTokenMiddleware());
        var entered = Signal();
        var canceled = Signal();
        var release = Signal();
        var first = scheduler.Enqueue<int>(async (_, token) =>
        {
            entered.TrySetResult(true);
            try
            { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException)
            {
                canceled.TrySetResult(true);
                await release.Task; // Delay actual termination after cancellation notification.
                throw;
            }
            finally { events.Enqueue("producer:terminated"); }
            return 42;
        }, "key", caller.Token);
        try
        {
            await entered.Task;
            var follower = scheduler.Enqueue<int>((_, _) => throw new AssertionException("Follower executed."), "key", default);
            await caller.CancelAsync();
            await Assert.ThatAsync(async () => await first, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(follower.IsCompleted, Is.False);
            Assert.That(canceled.Task.IsCompleted, Is.False, "Caller cancellation reached the shared producer.");
            if (cancellation == OwnershipCancellation.LocalScope)
                await localOwner.CancelAsync();
            else
                await sharedLane.CancelAsync();
            await canceled.Task;
            await Assert.ThatAsync(async () => await follower, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(ownership.ActiveCount, Is.EqualTo(1));
            Assert.That(ownership.Released, Is.Zero);
            var joining = scheduler.Enqueue<int>((_, _) => throw new AssertionException("A successor overlapped the terminating producer."), "key", default);
            await Assert.ThatAsync(async () => await joining, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(terminal.EnqueueCount, Is.EqualTo(1));
            var shutdown = lifetime.DisposeAsync().AsTask();
            Assert.That(shutdown.IsCompleted, Is.False);
            Assert.That(ownership.Cleanups, Is.Zero);
            Assert.That(events, Is.Empty);
            release.SetResult(true);
            Assert.That(await ownership.ProducerObserved, Is.InstanceOf<OperationCanceledException>());
            await shutdown;
            await lifetime.DisposeAsync();
            Assert.That(ownership.ActiveCount, Is.Zero);
            Assert.That(ownership.Released, Is.EqualTo(1));
            Assert.That(ownership.Cleanups, Is.EqualTo(1));
            Assert.That(events.ToArray(), Is.EqualTo(ProducerCleanupOrder));
        }
        finally { release.TrySetResult(true); await localOwner.CancelAsync(); await lifetime.DisposeAsync(); }
    }

    [Test]
    public async Task SingleFlight_AllWaitersCancel_LateProducerFaultIsObservedBeforeCleanup()
    {
        await using var lifetime = new TaskSchedulerMiddlewareLifetime();
        using var callers = new CancellationTokenSource();
        var events = new ConcurrentQueue<string>();
        var middleware = new OwnedSingleFlight(events, default, default);
        var scheduler = new CountingInlineTerminal().UseMiddleware(new OwnedFactory(middleware), lifetime);
        var entered = Signal();
        var release = Signal();
        var failure = new FormatException("after caller cancellation");
        var first = scheduler.Enqueue<int>(async (_, _) =>
        {
            entered.TrySetResult(true);
            await release.Task;
            events.Enqueue("producer:terminated");
            throw failure;
        }, "key", callers.Token);
        try
        {
            await entered.Task;
            var follower = scheduler.Enqueue<int>((_, _) => throw new AssertionException("Follower executed."), "key", callers.Token);
            await callers.CancelAsync();
            await Assert.ThatAsync(async () => await first, Throws.InstanceOf<OperationCanceledException>());
            await Assert.ThatAsync(async () => await follower, Throws.InstanceOf<OperationCanceledException>());
            var shutdown = lifetime.DisposeAsync().AsTask();
            Assert.That(shutdown.IsCompleted, Is.False);
            Assert.That(middleware.ActiveCount, Is.EqualTo(1));
            Assert.That(middleware.Cleanups, Is.Zero);
            release.SetResult(true);
            Assert.That(await middleware.ProducerObserved, Is.SameAs(failure));
            await shutdown;
            Assert.That(middleware.ActiveCount, Is.Zero);
            Assert.That(middleware.Released, Is.EqualTo(1));
            Assert.That(middleware.Cleanups, Is.EqualTo(1));
            Assert.That(events.ToArray(), Is.EqualTo(ProducerCleanupOrder));
        }
        finally { release.TrySetResult(true); await lifetime.DisposeAsync(); }
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
    public async Task Producer_PreservesLocalAndSharedOwnershipCancellation(OwnershipCancellation cancellation)
    {
        using var localOwner = new CancellationTokenSource();
        using var sharedLane = new CancellationTokenSource();
        using var caller = new CancellationTokenSource();
        var terminal = new CountingInlineTerminal();
        var ownership = new OwnershipTokenMiddleware(localOwner.Token, sharedLane.Token);
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
        await caller.CancelAsync();
        Assert.That(first.IsCompleted, Is.False, "Upstream replacement does not remove local/shared owner cancellation.");

        if (cancellation == OwnershipCancellation.LocalScope)
        {
            await localOwner.CancelAsync();
        }
        else
        {
            await sharedLane.CancelAsync();
        }

        Assert.That(async () => await first, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(terminal.EnqueueCount, Is.EqualTo(1));
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
            if (typeof(TResult) != typeof(int))
                throw new NotSupportedException("This test prototype supports Int32 results only.");
            var key = (string)context.State!;
            var candidate = new Producer();
            var producer = _producers.GetOrAdd(key, candidate);
            if (ReferenceEquals(candidate, producer))
            {
                return Lead();
            }

            return Wait<TResult>(producer.Task, context.CallerCancellationToken);

            async Task<TResult> Lead()
            {
                await Produce(key, producer, context, continuation);
                return await Wait<TResult>(producer.Task, context.CallerCancellationToken);
            }
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

    private sealed class OwnedFactory(ITaskSchedulerMiddleware middleware) : ITaskSchedulerMiddlewareFactory
    {
        public ITaskSchedulerMiddleware Create(TaskSchedulerMiddlewareLifetime lifetime) => middleware;
    }

    // A public-API seam, not a complete single-flight/promotion policy. The downstream path
    // in these tests returns actual terminal completion; nested caller waits are tested separately.
    // Keep the key through producer-scope cleanup and shared-result publication. Retirement
    // does not wait for the ownership boundary's observer or operation-wide disposal.
    private sealed class OwnedSingleFlight(ConcurrentQueue<string> events,
        CancellationToken localOwner, CancellationToken sharedLane, Action? beforeKeyReleased = null) : ITaskSchedulerEnqueueMiddleware, IDisposable
    {
        private readonly ConcurrentDictionary<string, Producer> _producers = new();
        private readonly TaskCompletionSource<Exception?> _observed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _released;
        private int _cleanups;

        public int ActiveCount => _producers.Count;
        public int Released => Volatile.Read(ref _released);
        public int Cleanups => Volatile.Read(ref _cleanups);
        public Task<Exception?> ProducerObserved => _observed.Task;

        public Task<T> InvokeAsync<T>(TaskSchedulerEnqueueContext<T> context, TaskSchedulerEnqueueDelegate<T> next)
        {
            if (typeof(T) != typeof(int))
                throw new NotSupportedException("This test prototype supports Int32 results only.");
            var key = (string)context.State!;
            var candidate = new Producer();
            var producer = _producers.GetOrAdd(key, candidate);
            if (!ReferenceEquals(candidate, producer))
                return Wait<T>(producer.Task, context.CallerCancellationToken);

            // Own the entire producer scope, including key retirement and cancellation-source
            // disposal. Owning only next while cleaning up outside that task would be too short.
            return context.InvokeWithCallerWait(
                ownedContext => Produce(key, producer, ownedContext, next),
                owned =>
                {
                    _ = Observe(owned(context));
                    return Wait<T>(producer.Task, context.CallerCancellationToken);
                });
        }

        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Transfer every producer failure to all followers and retain its original exception.")]
        private async Task<T> Produce<T>(string key, Producer producer, TaskSchedulerEnqueueContext<T> context,
            TaskSchedulerEnqueueDelegate<T> next)
        {
            try
            {
                T result;
                try
                {
                    using var ownership = CancellationTokenSource.CreateLinkedTokenSource(localOwner, sharedLane);
                    result = await next(context.WithCancellationToken(ownership.Token));
                }
                catch (Exception exception)
                {
                    producer.Completion.TrySetException(exception);
                    _ = producer.Task.Exception; // All caller waits may already have been canceled.
                    throw;
                }
                producer.Completion.TrySetResult((int)(object)result!);
                return result;
            }
            finally
            {
                // This test-only hook forces submissions into the publication/retirement window.
                try
                { beforeKeyReleased?.Invoke(); }
                finally
                {
                    _producers.TryRemove(new KeyValuePair<string, Producer>(key, producer));
                    Interlocked.Increment(ref _released);
                    events.Enqueue("key:released");
                }
            }
        }

        private async Task<T> Wait<T>(Task<int> producer, CancellationToken caller)
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(caller, localOwner, sharedLane);
            return (T)(object)await producer.WaitAsync(wait.Token);
        }

        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Observe the owned producer even when every caller stops waiting.")]
        private async Task Observe(Task producer)
        {
            try
            { await producer; _observed.TrySetResult(null); }
            catch (Exception exception) { _observed.TrySetResult(exception); }
        }

        public void Dispose()
        {
            Interlocked.Increment(ref _cleanups);
            events.Enqueue("cleanup");
        }

        private sealed class Producer
        {
            public TaskCompletionSource<int> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public Task<int> Task => Completion.Task;
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
            if (!_active.TryAdd(key, null))
                throw new InvalidOperationException("An equivalent operation is already active.");
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
                lock (_sync)
                    return _lanes.Count;
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
            lock (_sync)
                _isDisposed = true;
        }

        private void UpdateMaximum(int value)
        {
            int current;
            do
            {
                current = _maximumSameKeyConcurrency;
                if (current >= value)
                    return;
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

    private sealed class OwnershipTokenMiddleware(CancellationToken localOwnerToken, CancellationToken sharedLaneToken) : ITaskSchedulerEnqueueMiddleware
    {
        public async Task<TResult> InvokeAsync<TResult>(TaskSchedulerEnqueueContext<TResult> context, TaskSchedulerEnqueueDelegate<TResult> continuation)
        {
            using var ownership = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, localOwnerToken, sharedLaneToken);
            return await continuation(context.WithCancellationToken(ownership.Token));
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
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);
        public ValueTask<TaskSchedulerOperationOutcome<TResult>> InvokeAsync<TResult>(TaskSchedulerOperationContext context, TaskSchedulerOperationOutcome<TResult> outcome, TaskSchedulerCompletionDelegate<TResult> continuation)
        {
            Interlocked.Increment(ref _callCount);
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