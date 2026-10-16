namespace TaskFlow.Tests.Extensions
{
    using NUnit.Framework;
    using System.Threading.Tasks.Flow;

    [TestFixture]
    [CancelAfter(10000)]
    internal sealed class MiddlewareLifetimeFixture
    {
        private static readonly int[] SharedCleanupOrder = [1, 1];
        private static readonly int[] ReverseCleanupOrder = [3, 2, 1];
        private static readonly int[] SingleCleanup = [1];
        private static TaskCompletionSource<bool> Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        [Test]
        public async Task SnapshotsShareRegistrations_IndependentFactoriesOwnIndependentInstances_CallerObjectsAreNotOwned()
        {
            var flow = new TaskFlow();
            var cleanups = new List<int>();
            var factory = new Factory(lifetime => new OwnedMiddleware(1, cleanups));
            await using var callerOwned = new OwnedMiddleware(0, cleanups);
            var common = flow.UseMiddleware(factory);
            var first = common.UseMiddleware(callerOwned);
            var second = common.WithOperationName("branch");
            var independent = flow.UseMiddleware(factory);
            await Task.WhenAll(first.Enqueue(() => 1), second.Enqueue(() => 2), independent.Enqueue(() => 3));
            await flow.DisposeAsync();
            await flow.DisposeAsync();
            Assert.That(factory.Created, Is.EqualTo(2));
            Assert.That(cleanups, Is.EqualTo(SharedCleanupOrder));
        }

        [Test]
        public async Task Cleanup_WaitsForSubmissions_AttemptsAllInReverseOrder_AndSharesFailure()
        {
            var flow = new TaskFlow();
            var cleanups = new List<int>();
            var started = Gate();
            var release = Gate();
            var scheduler = flow
                .UseMiddleware(new Factory(_ => new OwnedMiddleware(1, cleanups)))
                .UseMiddleware(new Factory(_ => new OwnedMiddleware(2, cleanups, fail: true)))
                .UseMiddleware(new Factory(_ => new OwnedMiddleware(3, cleanups)));
            var operation = scheduler.Enqueue(async (CancellationToken _) => { started.SetResult(true); await release.Task; });
            try
            {
                await started.Task;
                var shutdown = flow.DisposeAsync().AsTask();
                var repeated = flow.DisposeAsync().AsTask();
                Assert.That(cleanups, Is.Empty);
                Assert.That(shutdown.IsCompleted, Is.False);
                release.SetResult(true);
                await operation;
                await Assert.ThatAsync(async () => await shutdown, Throws.TypeOf<AggregateException>());
                await Assert.ThatAsync(async () => await repeated, Throws.TypeOf<AggregateException>());
                Assert.That(cleanups, Is.EqualTo(ReverseCleanupOrder));
            }
            finally { release.TrySetResult(true); }
        }

        [Test]
        public async Task PreterminalAndShortCircuitedWork_IsDrainedBeforeCleanup()
        {
            var flow = new TaskFlow();
            StopAwareMiddleware middleware = null!;
            var scheduler = flow.UseMiddleware(new Factory(lifetime => middleware = new StopAwareMiddleware { Lifetime = lifetime }));
            var operation = scheduler.Enqueue((Func<int>)(() => throw new AssertionException("Should short circuit")));
            await middleware.Entered.Task;
            var shutdown = flow.DisposeAsync().AsTask();
            Assert.That(await operation, Is.EqualTo(17));
            await shutdown;
            Assert.That(middleware.Cleaned, Is.True);
        }

        [Test]
        public async Task RegistrationRacingShutdown_IsOwnedAndCleanedExactlyOnce()
        {
            var flow = new TaskFlow();
            var cleanups = new List<int>();
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var registration = Task.Run(() => flow.UseMiddleware(new Factory(_ =>
            {
                entered.Set();
                release.Wait();
                return new OwnedMiddleware(1, cleanups);
            })));
            try
            {
                Assert.That(entered.Wait(2000), Is.True);
                var shutdown = flow.DisposeAsync().AsTask();
                Assert.That(shutdown.IsCompleted, Is.False);
                release.Set();
                await Assert.ThatAsync(async () => await registration, Throws.TypeOf<ObjectDisposedException>());
                await shutdown;
                Assert.That(cleanups, Is.EqualTo(SingleCleanup));
            }
            finally { release.Set(); await flow.DisposeAsync(); }
        }

        [Test]
        public async Task ExplicitLifetime_IsRequiredForNonFlowTerminal_AndOwnsCleanup()
        {
            var terminal = new TaskFlowSchedulerAdapter(TaskScheduler.Default);
            var cleanups = new List<int>();
            var factory = new Factory(_ => new OwnedMiddleware(1, cleanups));
            Assert.That(() => terminal.UseMiddleware(factory), Throws.TypeOf<InvalidOperationException>());
            await using (var lifetime = new TaskSchedulerMiddlewareLifetime())
            {
                var scheduler = terminal.UseMiddleware(factory, lifetime);
                Assert.That(await scheduler.Enqueue(() => 42), Is.EqualTo(42));
                Assert.That(cleanups, Is.Empty);
            }
            Assert.That(cleanups, Is.EqualTo(SingleCleanup));
        }

        [Test]
        public async Task ReentrantDisposal_IsRejectedWithoutStoppingOwner()
        {
            await using var flow = new TaskFlow();
            var scheduler = flow.WithOperationName("reentrant");
            await scheduler.Enqueue(async (CancellationToken _) =>
            {
                await Assert.ThatAsync(async () => await flow.DisposeAsync(), Throws.TypeOf<InvalidOperationException>());
                await Task.CompletedTask;
            });
            Assert.That(await scheduler.Enqueue(() => 42), Is.EqualTo(42));
        }

        [Test]
        public async Task PendingCapacity_IsHeldDuringExecutionMiddlewareDelay()
        {
            await using var flow = new TaskFlow();
            var middleware = new DelayedExecutionMiddleware();
            var admission = new BoundedAdmissionController(new BoundedAdmissionOptions { Capacity = 1, CapacityMode = TaskSchedulerCapacityMode.Pending });
            var scheduler = flow.WithBoundedAdmission(admission).UseMiddleware(middleware);
            var first = scheduler.Enqueue(() => 1);
            try
            {
                await middleware.Entered.Task;
                var rejected = admission.Submit(scheduler, () => 2, TaskSchedulerAdmissionMode.Reject);
                Assert.That(await rejected.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Full));
                await Assert.ThatAsync(async () => await rejected.Completion, Throws.TypeOf<AdmissionRejectedException>());
                middleware.Release.SetResult(true);
                Assert.That(await first, Is.EqualTo(1));
            }
            finally { middleware.Release.TrySetResult(true); }
        }

        [Test]
        public async Task UpstreamDelay_PreservesTargetedModeAndState_AndDelaysAdmissionDecision()
        {
            await using var flow = new TaskFlow();
            var admission = new BoundedAdmissionController(new BoundedAdmissionOptions { Capacity = 1 });
            var middleware = new DelayedEnqueueMiddleware();
            var state = new object();
            var scheduler = flow.WithBoundedAdmission(admission).UseMiddleware(middleware).WithOperationName("late");
            var submission = admission.Submit(scheduler, (s, _) => new ValueTask<bool>(ReferenceEquals(s, state)), state, TaskSchedulerAdmissionMode.Reject, default);
            Assert.That(submission.Admission.IsCompleted, Is.False);
            middleware.Release.SetResult(true);
            Assert.That(await submission.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
            Assert.That(await submission.Completion, Is.True);
            Assert.That(middleware.State, Is.SameAs(state));
        }

        [Test]
        public async Task SchedulingFailure_ReleasesCapacityWithoutInvokingDelegate()
        {
            await using var lifetime = new TaskSchedulerMiddlewareLifetime();
            var admission = new BoundedAdmissionController(new BoundedAdmissionOptions { Capacity = 1 });
            var scheduler = new FailingTerminal().WithBoundedAdmission(admission, lifetime);
            for (var index = 0; index < 3; index++)
            {
                var submission = admission.Submit(scheduler, (Action)(() => throw new AssertionException("Must not run")), TaskSchedulerAdmissionMode.Wait);
                Assert.That(await submission.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
                await Assert.ThatAsync(async () => await submission.Completion, Throws.TypeOf<FormatException>());
            }
        }

        [Test]
        public async Task MultipleControllers_OverrideOnlyTheSelectedRegistration_AndRejectInvalidPairing()
        {
            await using var flow = new TaskFlow();
            var first = new BoundedAdmissionController(new BoundedAdmissionOptions { Capacity = 2 });
            var second = new BoundedAdmissionController(new BoundedAdmissionOptions { Capacity = 1, AdmissionMode = TaskSchedulerAdmissionMode.Reject });
            var scheduler = flow.WithBoundedAdmission(first).WithBoundedAdmission(second);
            var gate = Gate();
            var operation = scheduler.Enqueue((CancellationToken _) => gate.Task);
            try
            {
                var submission = first.Submit(scheduler, () => 2, TaskSchedulerAdmissionMode.Wait);
                // The outer controller still uses Reject. The selected inner gate is never reached.
                await Assert.ThatAsync(async () => await submission.Completion, Throws.TypeOf<AdmissionRejectedException>());
                await Assert.ThatAsync(async () => await submission.Admission, Throws.TypeOf<AdmissionRejectedException>());
                Assert.That(() => first.Submit(flow, () => 3, TaskSchedulerAdmissionMode.Reject), Throws.TypeOf<ArgumentException>());
            }
            finally { gate.SetResult(true); await operation; }
        }

        [Test]
        public async Task CallerTimeoutBeforeAdmission_DoesNotEraseLaterUnderlyingAdmission()
        {
            await using var flow = new TaskFlow();
            var admission = new BoundedAdmissionController(new BoundedAdmissionOptions { Capacity = 1 });
            var delay = new DelayedEnqueueMiddleware();
            var scheduler = flow.WithBoundedAdmission(admission)
                .UseMiddleware(delay)
                .UseMiddleware(new IgnoreProducerCancellation())
                .WithTimeout(TimeSpan.FromMilliseconds(100));
            var submission = admission.Submit(scheduler, () => 42, TaskSchedulerAdmissionMode.Reject);
            try
            {
                await Assert.ThatAsync(async () => await submission.Completion, Throws.TypeOf<TimeoutException>());
                Assert.That(submission.Admission.IsCompleted, Is.False);
                delay.Release.SetResult(true);
                Assert.That(await submission.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
            }
            finally { delay.Release.TrySetResult(true); }
        }

        [Test]
        public async Task AsyncCleanup_IsAwaitedBeforeDisposalCompletes()
        {
            var flow = new TaskFlow();
            AsyncCleanupMiddleware cleanup = null!;
            _ = flow.UseMiddleware(new Factory(_ => cleanup = new AsyncCleanupMiddleware()));
            var shutdown = flow.DisposeAsync().AsTask();
            await cleanup.Started.Task;
            try
            {
                Assert.That(shutdown.IsCompleted, Is.False);
                cleanup.Release.SetResult(true);
                await shutdown;
            }
            finally { cleanup.Release.TrySetResult(true); }
        }

        [Test]
        public async Task ConcurrentReleases_DispatchFifoWaitersInOrder()
        {
            await using var lifetime = new TaskSchedulerMiddlewareLifetime();
            var terminal = new TaskFlowSchedulerAdapter(TaskScheduler.Default);
            var recorder = new DispatchRecorder();
            var admission = new BoundedAdmissionController(new BoundedAdmissionOptions { Capacity = 4 });
            var scheduler = terminal.UseMiddleware(recorder).WithBoundedAdmission(admission, lifetime);
            var release = Gate();
            var operations = Enumerable.Range(0, 104).Select(index => scheduler.Enqueue<int>(
                async (_, _) => { if (index < 4) await release.Task; return index; }, index, default)).ToArray();
            Assert.That(recorder.Order, Has.Count.EqualTo(4));
            release.SetResult(true);
            await Task.WhenAll(operations);
            Assert.That(recorder.Order, Is.EqualTo(Enumerable.Range(0, 104)));
        }

        [Test]
        public async Task CurrentThreadAndCustomScheduler_AffinityIsPreserved()
        {
            await using var threadFlow = new CurrentThreadTaskFlow();
            var thread = new Thread(threadFlow.Run) { IsBackground = true };
            var bounded = threadFlow.WithBoundedAdmission(new BoundedAdmissionOptions { Capacity = 1 });
            var operation = bounded.Enqueue(() => Environment.CurrentManagedThreadId);
            thread.Start();
            Assert.That(await operation, Is.EqualTo(thread.ManagedThreadId));
            await threadFlow.DisposeAsync();

            var pair = new ConcurrentExclusiveSchedulerPair();
            await using var poolFlow = new TaskFlow(new TaskFlowOptions { TaskScheduler = pair.ExclusiveScheduler });
            var custom = poolFlow.WithBoundedAdmission(new BoundedAdmissionOptions { Capacity = 1 });
            Assert.That(await custom.Enqueue(() => TaskScheduler.Current), Is.SameAs(pair.ExclusiveScheduler));
            await poolFlow.DisposeAsync();
            pair.Complete();
            await pair.Completion;
        }

        [Test]
        public async Task AsyncConvenienceOverloads_AwaitTheirUnderlyingOperation()
        {
            await using var flow = new TaskFlow();
            var admission = new BoundedAdmissionController(new BoundedAdmissionOptions { Capacity = 1 });
            var scheduler = flow.WithBoundedAdmission(admission);
            var release = Gate();
            async Task<int> TaskOperation() { await release.Task; return 42; }
            async ValueTask<int> ValueTaskOperation() { await release.Task; return 43; }
            var taskSubmission = admission.Submit(scheduler, TaskOperation, TaskSchedulerAdmissionMode.Wait);
            try
            {
                Assert.That(await taskSubmission.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
                var valueTaskSubmission = admission.Submit(scheduler, ValueTaskOperation, TaskSchedulerAdmissionMode.Wait);
                Assert.That(taskSubmission.Completion.IsCompleted, Is.False);
                Assert.That(valueTaskSubmission.Admission.IsCompleted, Is.False);
                release.SetResult(true);
                Assert.That(await taskSubmission.Completion, Is.EqualTo(42));
                Assert.That(await valueTaskSubmission.Completion, Is.EqualTo(43));
            }
            finally { release.TrySetResult(true); }
        }

        [Test]
        public async Task ClosedEntry_DoesNotInvokeAlreadyCleanedMiddleware()
        {
            var flow = new TaskFlow();
            var observer = new CompletionObserver();
            var scheduler = flow.UseMiddleware(observer);
            await flow.DisposeAsync();
            await Assert.ThatAsync(async () => await scheduler.Enqueue(() => 42), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(observer.Invocations, Is.Zero);
            Assert.That(() => scheduler.UseMiddleware(observer), Throws.TypeOf<ObjectDisposedException>());
        }

        [TestCase("success")]
        [TestCase("failure")]
        [TestCase("cancellation")]
        public async Task UnreachedAdmission_IsSettledAtQuiescenceBeforeShutdownCompletes(string outcome)
        {
            await using var flow = new TaskFlow();
            var admission = new BoundedAdmissionController(new BoundedAdmissionOptions { Capacity = 1 });
            Exception? failure = outcome switch
            {
                "failure" => new FormatException("Short circuit failure"),
                "cancellation" => new OperationCanceledException(),
                _ => null
            };
            var middleware = new QuiescingShortCircuit(failure);
            var scheduler = flow.WithBoundedAdmission(admission).UseMiddleware(middleware);
            var submission = admission.Submit(scheduler, () => 42, TaskSchedulerAdmissionMode.Wait);
            var shutdown = flow.DisposeAsync().AsTask();
            try
            {
                Assert.That(shutdown.IsCompleted, Is.False);
                middleware.Release.SetResult(true);
                await shutdown;
                Assert.That(await middleware.DecisionAtQuiescence.Task, Is.True);
                Assert.That(submission.Admission.IsCompleted, Is.True);
                if (failure == null)
                {
                    Assert.That(await submission.Completion, Is.EqualTo(17));
                    await Assert.ThatAsync(async () => await submission.Admission, Throws.TypeOf<InvalidOperationException>());
                }
                else if (failure is OperationCanceledException)
                {
                    await Assert.ThatAsync(async () => await submission.Completion, Throws.InstanceOf<OperationCanceledException>());
                    await Assert.ThatAsync(async () => await submission.Admission, Throws.InstanceOf<OperationCanceledException>());
                    Assert.That(submission.Completion.IsCanceled, Is.True);
                    Assert.That(submission.Admission.IsCanceled, Is.True);
                }
                else
                {
                    await Assert.ThatAsync(async () => await submission.Completion, Throws.Exception.SameAs(failure));
                    await Assert.ThatAsync(async () => await submission.Admission, Throws.Exception.SameAs(failure));
                }
            }
            finally { middleware.Release.TrySetResult(true); }
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task UnreachedAdmission_RecordsTheOutcomeAfterCompletionMiddleware(bool recover)
        {
            await using var flow = new TaskFlow();
            var admission = new BoundedAdmissionController(new BoundedAdmissionOptions { Capacity = 1 });
            var original = new FormatException("Original failure");
            var replacement = new FormatException("Replacement failure");
            var middleware = new QuiescingShortCircuit(original);
            var scheduler = flow.WithBoundedAdmission(admission)
                .UseMiddleware(middleware)
                .UseMiddleware(new TransformCompletion(recover, replacement));
            var submission = admission.Submit(scheduler, () => 42, TaskSchedulerAdmissionMode.Wait);
            try
            {
                middleware.Release.SetResult(true);
                if (recover)
                {
                    Assert.That(await submission.Completion, Is.EqualTo(23));
                    await Assert.ThatAsync(async () => await submission.Admission, Throws.TypeOf<InvalidOperationException>());
                }
                else
                {
                    await Assert.ThatAsync(async () => await submission.Completion, Throws.Exception.SameAs(replacement));
                    await Assert.ThatAsync(async () => await submission.Admission, Throws.Exception.SameAs(replacement));
                }
                Assert.That(await middleware.DecisionAtQuiescence.Task, Is.True);
            }
            finally { middleware.Release.TrySetResult(true); }
        }

        [Test]
        public async Task CallerTimeout_UnreachedAdmissionWaitsForUnderlyingQuiescenceAndKeepsCallerFailure()
        {
            await using var flow = new TaskFlow();
            var admission = new BoundedAdmissionController(new BoundedAdmissionOptions { Capacity = 1 });
            var middleware = new QuiescingShortCircuit(null);
            var scheduler = flow.WithBoundedAdmission(admission).UseMiddleware(middleware).WithTimeout(TimeSpan.FromMilliseconds(100));
            var submission = admission.Submit(scheduler, () => 42, TaskSchedulerAdmissionMode.Wait);
            try
            {
                await Assert.ThatAsync(async () => await submission.Completion, Throws.TypeOf<TimeoutException>());
                Assert.That(submission.Admission.IsCompleted, Is.False);
                var shutdown = flow.DisposeAsync().AsTask();
                Assert.That(shutdown.IsCompleted, Is.False);
                middleware.Release.SetResult(true);
                await shutdown;
                Assert.That(await middleware.DecisionAtQuiescence.Task, Is.True);
                await Assert.ThatAsync(async () => await submission.Admission, Throws.TypeOf<TimeoutException>());
            }
            finally { middleware.Release.TrySetResult(true); }
        }

        [Test]
        public async Task ClosedEntry_SettlesExplicitAdmissionWithoutInvokingCompletionMiddleware()
        {
            var flow = new TaskFlow();
            var admission = new BoundedAdmissionController(new BoundedAdmissionOptions { Capacity = 1 });
            var observer = new CompletionObserver();
            var scheduler = flow.WithBoundedAdmission(admission).UseMiddleware(observer);
            await flow.DisposeAsync();
            var submission = admission.Submit(scheduler, () => 42, TaskSchedulerAdmissionMode.Wait);
            Assert.That(submission.Admission.IsCompleted, Is.True);
            await Assert.ThatAsync(async () => await submission.Admission, Throws.TypeOf<ObjectDisposedException>());
            await Assert.ThatAsync(async () => await submission.Completion, Throws.TypeOf<ObjectDisposedException>());
            Assert.That(observer.Invocations, Is.Zero);
        }

        private sealed class QuiescingShortCircuit(Exception? failure) : ITaskSchedulerEnqueueMiddleware
        {
            public TaskCompletionSource<bool> Release { get; } = Gate();
            public TaskCompletionSource<bool> DecisionAtQuiescence { get; } = Gate();
            public async Task<T> InvokeAsync<T>(TaskSchedulerEnqueueContext<T> context, TaskSchedulerEnqueueDelegate<T> next)
            {
                context.Operation.OnQuiesced(() => DecisionAtQuiescence.TrySetResult(context.Operation.AdmissionRequest!.Decision.IsCompleted));
                await Release.Task;
                if (failure != null) throw failure;
                return (T)(object)17;
            }
        }

        private sealed class TransformCompletion(bool recover, Exception replacement) : ITaskSchedulerCompletionMiddleware
        {
            public ValueTask<TaskSchedulerOperationOutcome<T>> InvokeAsync<T>(TaskSchedulerOperationContext context, TaskSchedulerOperationOutcome<T> outcome, TaskSchedulerCompletionDelegate<T> next)
                => next(context, recover
                    ? TaskSchedulerOperationOutcome<T>.FromResult((T)(object)23)
                    : TaskSchedulerOperationOutcome<T>.FromException(replacement));
        }

        private sealed class CompletionObserver : ITaskSchedulerCompletionMiddleware
        {
            public int Invocations { get; private set; }
            public ValueTask<TaskSchedulerOperationOutcome<T>> InvokeAsync<T>(TaskSchedulerOperationContext context, TaskSchedulerOperationOutcome<T> outcome, TaskSchedulerCompletionDelegate<T> next)
            {
                Invocations++;
                return next(context, outcome);
            }
        }

        private sealed class IgnoreProducerCancellation : ITaskSchedulerEnqueueMiddleware
        {
            public Task<T> InvokeAsync<T>(TaskSchedulerEnqueueContext<T> context, TaskSchedulerEnqueueDelegate<T> next)
                => next(context.WithCancellationToken(CancellationToken.None));
        }

        private sealed class AsyncCleanupMiddleware : ITaskSchedulerEnqueueMiddleware, IAsyncDisposable
        {
            public TaskCompletionSource<bool> Started { get; } = Gate();
            public TaskCompletionSource<bool> Release { get; } = Gate();
            public Task<T> InvokeAsync<T>(TaskSchedulerEnqueueContext<T> context, TaskSchedulerEnqueueDelegate<T> next) => next(context);
            public async ValueTask DisposeAsync() { Started.SetResult(true); await Release.Task; }
        }

        private sealed class DispatchRecorder : ITaskSchedulerEnqueueMiddleware
        {
            public List<int> Order { get; } = new();
            public Task<T> InvokeAsync<T>(TaskSchedulerEnqueueContext<T> context, TaskSchedulerEnqueueDelegate<T> next)
            {
                Order.Add((int)context.State!);
                return next(context);
            }
        }

        private sealed class Factory(Func<TaskSchedulerMiddlewareLifetime, ITaskSchedulerMiddleware> create) : ITaskSchedulerMiddlewareFactory
        {
            public int Created { get; private set; }
            public ITaskSchedulerMiddleware Create(TaskSchedulerMiddlewareLifetime lifetime) { Created++; return create(lifetime); }
        }

        private sealed class OwnedMiddleware(int id, List<int> cleanups, bool fail = false) : ITaskSchedulerEnqueueMiddleware, IAsyncDisposable
        {
            public Task<T> InvokeAsync<T>(TaskSchedulerEnqueueContext<T> context, TaskSchedulerEnqueueDelegate<T> next) => next(context);
            public ValueTask DisposeAsync()
            {
                cleanups.Add(id);
                if (fail) throw new FormatException("Cleanup failure");
                return default;
            }
        }

        private sealed class StopAwareMiddleware : ITaskSchedulerEnqueueMiddleware, IAsyncDisposable
        {
            public TaskSchedulerMiddlewareLifetime Lifetime { get; set; } = null!;
            public TaskCompletionSource<bool> Entered { get; } = Gate();
            public bool Cleaned { get; private set; }
            public async Task<T> InvokeAsync<T>(TaskSchedulerEnqueueContext<T> context, TaskSchedulerEnqueueDelegate<T> next)
            {
                Entered.SetResult(true);
                var stopping = Gate();
                using var cancellation = Lifetime.StoppingToken.Register(() => stopping.TrySetResult(true));
                await stopping.Task;
                Assert.That(Cleaned, Is.False);
                return (T)(object)17;
            }
            public ValueTask DisposeAsync() { Cleaned = true; return default; }
        }

        private sealed class DelayedExecutionMiddleware : ITaskSchedulerExecutionMiddleware
        {
            public TaskCompletionSource<bool> Entered { get; } = Gate();
            public TaskCompletionSource<bool> Release { get; } = Gate();
            public async ValueTask<T> InvokeAsync<T>(TaskSchedulerOperationContext context, TaskSchedulerExecutionDelegate<T> next)
            {
                Entered.TrySetResult(true);
                await Release.Task;
                return await next(context);
            }
        }

        private sealed class DelayedEnqueueMiddleware : ITaskSchedulerEnqueueMiddleware
        {
            public TaskCompletionSource<bool> Release { get; } = Gate();
            public object? State { get; private set; }
            public async Task<T> InvokeAsync<T>(TaskSchedulerEnqueueContext<T> context, TaskSchedulerEnqueueDelegate<T> next)
            {
                State = context.State;
                await Release.Task;
                return await next(context);
            }
        }

        private sealed class FailingTerminal : ITaskScheduler
        {
            public Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
                => throw new FormatException("Scheduling failed");
        }
    }
}
