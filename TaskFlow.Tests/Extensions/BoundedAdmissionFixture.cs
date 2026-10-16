namespace TaskFlow.Tests.Extensions
{
    using NUnit.Framework;
    using System.Threading.Tasks.Flow;

    // NUnit accepts constructor arguments through an object array.
#pragma warning disable CS3016
    [TestFixture(false)]
    [TestFixture(true)]
#pragma warning restore CS3016
    [CancelAfter(10000)]
    internal sealed class BoundedAdmissionFixture(bool dedicatedThread)
    {
        private static readonly int[] FifoOrder = [1, 2, 3, 4];
        private TaskFlowBase CreateFlow() => dedicatedThread ? new DedicatedThreadTaskFlow() : new TaskFlow();
        private static TaskCompletionSource<int> Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static BoundedAdmissionController Controller(int capacity = 1, TaskSchedulerCapacityMode mode = TaskSchedulerCapacityMode.Outstanding, int? waiters = null, TaskSchedulerAdmissionMode admissionMode = TaskSchedulerAdmissionMode.Wait)
            => new(new BoundedAdmissionOptions { Capacity = capacity, CapacityMode = mode, MaxAdmissionWaiters = waiters, AdmissionMode = admissionMode });

        [Test]
        public async Task EnqueueAndSubmit_ShareCapacity_DefaultWaitAndExplicitReject()
        {
            await using var flow = CreateFlow();
            var admission = Controller();
            var scheduler = flow.WithBoundedAdmission(admission).WithOperationName("save");
            var gate = Gate();
            var started = Gate();
            var invoked = 0;
            var first = scheduler.Enqueue<int>((_, _) => { started.SetResult(0); return new(gate.Task); }, null, default);
            try
            {
                await started.Task;
                var second = scheduler.Enqueue(() => Interlocked.Increment(ref invoked));
                var rejected = admission.Submit(scheduler, () => Interlocked.Increment(ref invoked), TaskSchedulerAdmissionMode.Reject);
                Assert.That(rejected.Admission.IsCompletedSuccessfully, Is.True);
                Assert.That(await rejected.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Full));
                await Assert.ThatAsync(async () => await rejected.Completion, Throws.TypeOf<AdmissionRejectedException>());
                Assert.That(second.IsCompleted, Is.False);
                Assert.That(invoked, Is.Zero);
                gate.SetResult(42);
                Assert.That(await first, Is.EqualTo(42));
                Assert.That(await second, Is.EqualTo(1));
            }
            finally { gate.TrySetResult(0); }
        }

        [TestCase(TaskSchedulerCapacityMode.Outstanding)]
        [TestCase(TaskSchedulerCapacityMode.Pending)]
        public async Task CapacityAndFifo_AreBasedOnActualExecution(TaskSchedulerCapacityMode mode)
        {
            await using var flow = CreateFlow();
            var admission = Controller(mode == TaskSchedulerCapacityMode.Outstanding ? 2 : 1, mode);
            var scheduler = flow.WithBoundedAdmission(admission);
            var firstGate = Gate();
            var secondGate = Gate();
            var firstStarted = Gate();
            var secondStarted = Gate();
            var order = new List<int>();
            var first = scheduler.Enqueue<int>((_, _) => { order.Add(1); firstStarted.SetResult(0); return new(firstGate.Task); }, null, default);
            try
            {
                await firstStarted.Task;
                var second = admission.Submit<int>(scheduler, (_, _) => { order.Add(2); secondStarted.SetResult(0); return new(secondGate.Task); }, null, TaskSchedulerAdmissionMode.Wait, default);
                Assert.That(await second.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
                var third = admission.Submit(scheduler, () => { order.Add(3); return 3; }, TaskSchedulerAdmissionMode.Wait);
                var fourth = admission.Submit(scheduler, () => { order.Add(4); return 4; }, TaskSchedulerAdmissionMode.Wait);
                Assert.That(third.Admission.IsCompleted, Is.False);
                Assert.That(fourth.Admission.IsCompleted, Is.False);
                firstGate.SetResult(1);
                await secondStarted.Task;
                Assert.That(await third.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
                Assert.That(second.Completion.IsCompleted, Is.False);
                Assert.That(fourth.Admission.IsCompleted, Is.False);
                secondGate.SetResult(2);
                await Task.WhenAll(first, second.Completion, third.Completion, fourth.Completion);
                Assert.That(order, Is.EqualTo(FifoOrder));
            }
            finally { firstGate.TrySetResult(0); secondGate.TrySetResult(0); }
        }

        [Test]
        public async Task CanceledWaiter_IsNeverInvoked_AndDoesNotConsumeWaiterLimit()
        {
            await using var flow = CreateFlow();
            var admission = Controller(waiters: 1);
            var scheduler = flow.WithBoundedAdmission(admission);
            var gate = Gate();
            var first = scheduler.Enqueue<int>((_, _) => new(gate.Task), null, default);
            try
            {
                using var cancellation = new CancellationTokenSource();
                var canceled = admission.Submit(scheduler, (Action)(() => throw new AssertionException("Canceled waiter invoked")), TaskSchedulerAdmissionMode.Wait, cancellation.Token);
                var overflow = admission.Submit(scheduler, () => 2, TaskSchedulerAdmissionMode.Wait);
                Assert.That(await overflow.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.WaiterLimitReached));
                await Assert.ThatAsync(async () => await overflow.Completion, Throws.TypeOf<AdmissionRejectedException>());
                await cancellation.CancelAsync();
                await Assert.ThatAsync(async () => await canceled.Admission, Throws.InstanceOf<OperationCanceledException>());
                await Assert.ThatAsync(async () => await canceled.Completion, Throws.InstanceOf<OperationCanceledException>());
                var next = admission.Submit(scheduler, () => 3, TaskSchedulerAdmissionMode.Wait);
                Assert.That(next.Admission.IsCompleted, Is.False);
                gate.SetResult(1);
                await first;
                Assert.That(await next.Completion, Is.EqualTo(3));
            }
            finally { gate.TrySetResult(0); }
        }

        [Test]
        public async Task Dispose_ClosesWaitersAndSharesShutdown_UntilRunningWorkCompletes()
        {
            var flow = CreateFlow();
            var admission = Controller();
            var scheduler = flow.WithBoundedAdmission(admission);
            var gate = Gate();
            var started = Gate();
            var first = scheduler.Enqueue<int>((_, _) => { started.SetResult(0); return new(gate.Task); }, null, default);
            try
            {
                await started.Task;
                var waiter = admission.Submit(scheduler, (Action)(() => throw new AssertionException("Shutdown waiter invoked")), TaskSchedulerAdmissionMode.Wait);
                var shutdown = flow.DisposeAsync().AsTask();
                var again = flow.DisposeAsync().AsTask();
                Assert.That(await waiter.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Closed));
                await Assert.ThatAsync(async () => await waiter.Completion, Throws.TypeOf<AdmissionRejectedException>());
                Assert.That(shutdown.IsCompleted, Is.False);
                Assert.That(again.IsCompleted, Is.False);
                gate.SetResult(1);
                await Task.WhenAll(first, shutdown, again);
                await Assert.ThatAsync(async () => await scheduler.Enqueue(() => 0), Throws.TypeOf<ObjectDisposedException>());
            }
            finally { gate.TrySetResult(0); await flow.DisposeAsync(); }
        }

        [TestCase(TaskSchedulerCapacityMode.Outstanding, true)]
        [TestCase(TaskSchedulerCapacityMode.Outstanding, false)]
        [TestCase(TaskSchedulerCapacityMode.Pending, true)]
        [TestCase(TaskSchedulerCapacityMode.Pending, false)]
        public async Task Timeout_DoesNotFreeUnderlyingCapacity(TaskSchedulerCapacityMode mode, bool timeoutOutside)
        {
            await using var flow = CreateFlow();
            var admission = Controller(mode: mode);
            var gate = Gate();
            var started = Gate();
            var timeout = TimeSpan.FromMilliseconds(100);
            ITaskScheduler scheduler = timeoutOutside
                ? flow.WithBoundedAdmission(admission).WithTimeout(timeout)
                : flow.WithTimeout(timeout).WithBoundedAdmission(admission);
            var first = scheduler.Enqueue<int>((_, _) => { started.TrySetResult(0); return new(gate.Task); }, null, default);
            try
            {
                await started.Task;
                await Assert.ThatAsync(async () => await first, Throws.TypeOf<TimeoutException>());
                if (mode == TaskSchedulerCapacityMode.Pending)
                {
                    // The running operation no longer counts; its queued successor does, even after timeout.
                    var queued = admission.Submit(scheduler, () => 2, TaskSchedulerAdmissionMode.Reject);
                    Assert.That(await queued.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
                    await Assert.ThatAsync(async () => await queued.Completion, Throws.TypeOf<TimeoutException>());
                }
                var rejected = admission.Submit(scheduler, () => 3, TaskSchedulerAdmissionMode.Reject);
                Assert.That(await rejected.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Full));
                await Assert.ThatAsync(async () => await rejected.Completion, Throws.TypeOf<AdmissionRejectedException>());
                var shutdown = flow.DisposeAsync().AsTask();
                Assert.That(shutdown.IsCompleted, Is.False);
                gate.SetResult(1);
                await shutdown;
            }
            finally { gate.TrySetResult(0); }
        }

        [TestCase(TaskSchedulerCapacityMode.Outstanding)]
        [TestCase(TaskSchedulerCapacityMode.Pending)]
        public async Task AcceptedOutcomes_AreSeparateFromAdmission_AndReleaseSlots(TaskSchedulerCapacityMode mode)
        {
            await using var flow = CreateFlow();
            var admission = Controller(mode: mode);
            var scheduler = flow.WithBoundedAdmission(admission);
            var success = admission.Submit(scheduler, () => 42, TaskSchedulerAdmissionMode.Wait);
            Assert.That(await success.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
            Assert.That(await success.Completion, Is.EqualTo(42));
            var failure = admission.Submit(scheduler, (Func<int>)(() => throw new FormatException()), TaskSchedulerAdmissionMode.Wait);
            Assert.That(await failure.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
            await Assert.ThatAsync(async () => await failure.Completion, Throws.TypeOf<FormatException>());
            var canceled = admission.Submit(scheduler, (Func<int>)(() => throw new OperationCanceledException()), TaskSchedulerAdmissionMode.Wait);
            Assert.That(await canceled.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
            await Assert.ThatAsync(async () => await canceled.Completion, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(await admission.Submit(scheduler, () => 7, TaskSchedulerAdmissionMode.Wait).Completion, Is.EqualTo(7));
        }

        [Test]
        public async Task DefaultReject_CanBeOverriddenWithWait()
        {
            await using var flow = CreateFlow();
            var admission = Controller(admissionMode: TaskSchedulerAdmissionMode.Reject);
            var scheduler = flow.WithBoundedAdmission(admission);
            var gate = Gate();
            var first = scheduler.Enqueue<int>((_, _) => new(gate.Task), null, default);
            try
            {
                await Assert.ThatAsync(async () => await scheduler.Enqueue(() => 2), Throws.TypeOf<AdmissionRejectedException>());
                var waiting = admission.Submit(scheduler, () => 3, TaskSchedulerAdmissionMode.Wait);
                Assert.That(waiting.Admission.IsCompleted, Is.False);
                gate.SetResult(1);
                await first;
                Assert.That(await waiting.Completion, Is.EqualTo(3));
            }
            finally { gate.TrySetResult(0); }
        }

        [Test]
        public async Task SubmissionCancellationRacingAdmissionAndDisposal_DoesNotLeak()
        {
            for (var iteration = 0; iteration < 30; iteration++)
            {
                await using var flow = CreateFlow();
                var admission = Controller();
                var scheduler = flow.WithBoundedAdmission(admission);
                var gate = Gate();
                var first = scheduler.Enqueue<int>((_, _) => new(gate.Task), null, default);
                using var cancellation = new CancellationTokenSource();
                var second = admission.Submit(scheduler, () => 2, TaskSchedulerAdmissionMode.Wait, cancellation.Token);
                var release = Task.Run(() => gate.TrySetResult(1));
                var cancel = Task.Run(cancellation.Cancel);
                var dispose = Task.Run(async () => await flow.DisposeAsync());
                await Task.WhenAll(release, cancel, dispose);
                await first;
                try { await second.Completion; }
                catch (OperationCanceledException) { }
                catch (AdmissionRejectedException) { }
                catch (ObjectDisposedException) { }
                Assert.That(second.Admission.IsCompleted, Is.True);
                if (second.Admission.IsCanceled) await Assert.ThatAsync(async () => await second.Admission, Throws.InstanceOf<OperationCanceledException>());
            }
        }

        [Test]
        public async Task ExecutionAffinity_IsPreserved()
        {
            await using var flow = CreateFlow();
            var scheduler = flow.WithBoundedAdmission(new BoundedAdmissionOptions { Capacity = 1 });
            var thread = await scheduler.Enqueue(async (CancellationToken _) =>
            {
                var before = Environment.CurrentManagedThreadId;
                await Task.Yield();
                return (before, after: Environment.CurrentManagedThreadId);
            });
            if (flow is DedicatedThreadTaskFlow dedicated)
            {
                Assert.That(thread.before, Is.EqualTo(dedicated.ThreadId));
                Assert.That(thread.after, Is.EqualTo(dedicated.ThreadId));
            }
        }
    }
}
