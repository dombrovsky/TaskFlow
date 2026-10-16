namespace TaskFlow.Tests.Extensions;

using NUnit.Framework;
using System.Threading.Tasks.Flow;
using System.Threading.Tasks.Flow.Internal;

[CancelAfter(15000)]
internal sealed class KeyedSchedulingFixture
{
    private static readonly int[] StructKeyOrder = [10, 20, 30, 40];
    private static readonly string[] RoundRobinOrder = ["A1", "B1", "C1", "A2", "B2", "A3"];
    private static TaskCompletionSource<int> Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static BoundedAdmissionController Budget(int capacity, TaskSchedulerCapacityMode mode = TaskSchedulerCapacityMode.Pending, int? waiters = null)
        => new(new BoundedAdmissionOptions { Capacity = capacity, CapacityMode = mode, MaxAdmissionWaiters = waiters });
    private static Task<int> Hold(ITaskScheduler scheduler, string key, TaskCompletionSource<int> started, TaskCompletionSource<int> release)
        => scheduler.Enqueue(key, (Func<string, CancellationToken, Task<int>>)((_, _) => { started.TrySetResult(0); return release.Task; }));

    [Test]
    public async Task ReadyKeys_AreRoundRobin_AndEachKeyIsFifo()
    {
        await using var flow = new TaskFlow();
        var scheduler = flow.WithKeyedScheduling<string>().WithConcurrencyLimit(1);
        var release = Gate();
        var started = Gate();
        var order = new List<string>();
        var first = scheduler.Enqueue("A", (Func<Task<int>>)(() => { order.Add("A1"); started.SetResult(0); return release.Task; }));
        try
        {
            await started.Task;
            var tasks = new[]
            {
                scheduler.Enqueue("A", () => { order.Add("A2"); return 2; }),
                scheduler.Enqueue("A", () => { order.Add("A3"); return 3; }),
                scheduler.Enqueue("B", () => { order.Add("B1"); return 1; }),
                scheduler.Enqueue("B", () => { order.Add("B2"); return 2; }),
                scheduler.Enqueue("C", () => { order.Add("C1"); return 1; })
            };
            release.SetResult(0);
            await Task.WhenAll(tasks.Append(first));
            Assert.That(order, Is.EqualTo(RoundRobinOrder));
        }
        finally { release.TrySetResult(0); }
    }

    [Test]
    public async Task BusyKeyWaiters_DoNotTakeConcurrencySlots()
    {
        await using var flow = new TaskFlow();
        var scheduler = flow.WithKeyedScheduling<string>().WithConcurrencyLimit(2);
        var a = Gate();
        var b = Gate();
        var c = Gate();
        var aStarted = Gate();
        var bStarted = Gate();
        var cStarted = Gate();
        var nextAStarted = Gate();
        var first = Hold(scheduler, "A", aStarted, a);
        try
        {
            await aStarted.Task;
            var nextA = scheduler.Enqueue("A", () => nextAStarted.TrySetResult(0));
            var second = Hold(scheduler, "B", bStarted, b);
            await bStarted.Task;
            var third = Hold(scheduler, "C", cStarted, c);
            Assert.That(cStarted.Task.IsCompleted, Is.False);
            Assert.That(nextAStarted.Task.IsCompleted, Is.False);
            b.SetResult(0);
            await cStarted.Task;
            Assert.That(nextAStarted.Task.IsCompleted, Is.False);
            a.SetResult(0);
            c.SetResult(0);
            await Task.WhenAll(first, nextA, second, third);
        }
        finally { a.TrySetResult(0); b.TrySetResult(0); c.TrySetResult(0); }
    }

    [TestCase(TaskSchedulerCapacityMode.Pending)]
    [TestCase(TaskSchedulerCapacityMode.Outstanding)]
    public async Task CombinedBudgets_ReserveAtomically_AndSkipBlockedKeys(TaskSchedulerCapacityMode mode)
    {
        await using var flow = new TaskFlow();
        var global = Budget(mode == TaskSchedulerCapacityMode.Pending ? 2 : 3, mode);
        var perKey = Budget(1);
        var scheduler = flow.WithKeyedScheduling<string>().WithConcurrencyLimit(1)
            .WithBoundedAdmission(global).WithPerKeyBoundedAdmission(perKey);
        var release = Gate();
        var started = Gate();
        var first = Hold(scheduler, "A", started, release);
        try
        {
            await started.Task;
            var queued = global.Submit(scheduler, "A", () => 1, TaskSchedulerAdmissionMode.Reject);
            Assert.That(await queued.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
            var waiting = global.Submit(scheduler, "A", () => 2, TaskSchedulerAdmissionMode.Wait);
            Assert.That(waiting.Admission.IsCompleted, Is.False);
            // The blocked A waiter must reserve neither the global slot nor the execution slot.
            var other = global.Submit(scheduler, "B", () => 3, TaskSchedulerAdmissionMode.Reject);
            Assert.That(await other.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
            var full = perKey.Submit(scheduler, "C", () => 4, TaskSchedulerAdmissionMode.Reject);
            Assert.That(await full.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Full));
            await Assert.ThatAsync(async () => await full.Completion, Throws.TypeOf<AdmissionRejectedException>());
            release.SetResult(0);
            await Task.WhenAll(first, queued.Completion, waiting.Completion, other.Completion);
        }
        finally { release.TrySetResult(0); }
    }

    [Test]
    public async Task WaiterCancellation_ReleasesEveryWaiterBudget()
    {
        await using var flow = new TaskFlow();
        var global = Budget(1, waiters: 1);
        var perKey = Budget(1, waiters: 1);
        var scheduler = flow.WithKeyedScheduling<string>().WithConcurrencyLimit(1)
            .WithBoundedAdmission(global).WithPerKeyBoundedAdmission(perKey);
        var release = Gate();
        var started = Gate();
        var first = Hold(scheduler, "A", started, release);
        try
        {
            await started.Task;
            var queued = global.Submit(scheduler, "A", () => 1, TaskSchedulerAdmissionMode.Wait);
            await queued.Admission;
            using var cancellation = new CancellationTokenSource();
            var waiting = global.Submit(scheduler, "A", (Action)(() => throw new AssertionException("Canceled waiter invoked")), TaskSchedulerAdmissionMode.Wait, cancellation.Token);
            var overflow = global.Submit(scheduler, "A", () => 2, TaskSchedulerAdmissionMode.Wait);
            Assert.That(await overflow.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.WaiterLimitReached));
            await Assert.ThatAsync(async () => await overflow.Completion, Throws.TypeOf<AdmissionRejectedException>());
            await cancellation.CancelAsync();
            await Assert.ThatAsync(async () => await waiting.Admission, Throws.InstanceOf<OperationCanceledException>());
            await Assert.ThatAsync(async () => await waiting.Completion, Throws.InstanceOf<OperationCanceledException>());
            var next = global.Submit(scheduler, "A", () => 3, TaskSchedulerAdmissionMode.Wait);
            Assert.That(next.Admission.IsCompleted, Is.False);
            release.SetResult(0);
            await Task.WhenAll(first, queued.Completion, next.Completion);
        }
        finally { release.TrySetResult(0); }
    }

    [Test]
    public async Task PendingCapacity_EndsAtUserDelegateStart_NotDispatch()
    {
        await using var flow = new TaskFlow();
        var executionEntered = Gate();
        var permitStart = Gate();
        var started = Gate();
        var release = Gate();
        var admission = Budget(1);
        var scheduler = flow.WithKeyedScheduling<string>().WithConcurrencyLimit(1)
            .WithBoundedAdmission(admission).UseMiddleware(new ExecutionDelay(executionEntered, permitStart));
        var first = Hold(scheduler, "A", started, release);
        try
        {
            await executionEntered.Task;
            var full = admission.Submit(scheduler, "B", () => 1, TaskSchedulerAdmissionMode.Reject);
            Assert.That(await full.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Full));
            await Assert.ThatAsync(async () => await full.Completion, Throws.TypeOf<AdmissionRejectedException>());
            permitStart.SetResult(0);
            await started.Task;
            var pending = admission.Submit(scheduler, "B", () => 2, TaskSchedulerAdmissionMode.Reject);
            Assert.That(await pending.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
            Assert.That(pending.Completion.IsCompleted, Is.False);
            release.SetResult(0);
            await Task.WhenAll(first, pending.Completion);
        }
        finally { permitStart.TrySetResult(0); release.TrySetResult(0); }
    }

    [TestCase(TaskSchedulerCapacityMode.Pending, true)]
    [TestCase(TaskSchedulerCapacityMode.Pending, false)]
    [TestCase(TaskSchedulerCapacityMode.Outstanding, true)]
    [TestCase(TaskSchedulerCapacityMode.Outstanding, false)]
    public async Task Timeout_DoesNotReleaseKeyOrExecutionSlot(TaskSchedulerCapacityMode mode, bool timeoutOutside)
    {
        await using var flow = new TaskFlow();
        var admission = Budget(mode == TaskSchedulerCapacityMode.Pending ? 1 : 2, mode);
        var root = flow.WithKeyedScheduling<string>().WithConcurrencyLimit(1);
        var timeout = TimeSpan.FromMilliseconds(100);
        var core = timeoutOutside ? root.WithBoundedAdmission(admission) : root.WithTimeout(timeout).WithBoundedAdmission(admission);
        var scheduler = timeoutOutside ? core.WithTimeout(timeout) : core;
        var release = Gate();
        var started = Gate();
        var invoked = 0;
        var first = Hold(scheduler, "A", started, release);
        try
        {
            await started.Task;
            await Assert.ThatAsync(async () => await first, Throws.TypeOf<TimeoutException>());
            var same = admission.Submit(core, "A", () => Interlocked.Increment(ref invoked), TaskSchedulerAdmissionMode.Reject);
            Assert.That(await same.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
            var full = admission.Submit(core, "B", () => 3, TaskSchedulerAdmissionMode.Reject);
            Assert.That(await full.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Full));
            await Assert.ThatAsync(async () => await full.Completion, Throws.TypeOf<AdmissionRejectedException>());
            Assert.That(invoked, Is.Zero);
            release.SetResult(0);
            Assert.That(await same.Completion, Is.EqualTo(1));
        }
        finally { release.TrySetResult(0); }
    }

    [Test]
    public async Task Shutdown_ClosesWaiters_CancelsUndispatched_AndWaitsForUnderlyingWork()
    {
        var flow = new TaskFlow();
        var admission = Budget(1);
        var scheduler = flow.WithKeyedScheduling<string>().WithConcurrencyLimit(1).WithBoundedAdmission(admission);
        var release = Gate();
        var started = Gate();
        var first = Hold(scheduler, "A", started, release);
        try
        {
            await started.Task;
            var queued = admission.Submit(scheduler, "B", (Action)(() => throw new AssertionException("Queued shutdown work invoked")), TaskSchedulerAdmissionMode.Wait);
            Assert.That(await queued.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
            var waiter = admission.Submit(scheduler, "C", () => 3, TaskSchedulerAdmissionMode.Wait);
            var shutdown = flow.DisposeAsync().AsTask();
            var again = flow.DisposeAsync().AsTask();
            Assert.That(await waiter.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Closed));
            await Assert.ThatAsync(async () => await waiter.Completion, Throws.TypeOf<AdmissionRejectedException>());
            await Assert.ThatAsync(async () => await queued.Completion, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(shutdown.IsCompleted, Is.False);
            release.SetResult(0);
            await Task.WhenAll(first, shutdown, again);
            await Assert.ThatAsync(async () => await scheduler.Enqueue("D", () => 0), Throws.TypeOf<ObjectDisposedException>());
        }
        finally { release.TrySetResult(0); await flow.DisposeAsync(); }
    }

    [Test]
    public async Task Comparer_ControlsSerialization_ButDelegateReceivesOriginalKeyAndState()
    {
        await using var flow = new TaskFlow();
        var scheduler = flow.WithKeyedScheduling<string>(StringComparer.OrdinalIgnoreCase);
        var release = Gate();
        var started = Gate();
        var first = Hold(scheduler, "document", started, release);
        try
        {
            await started.Task;
            var state = new object();
            var second = scheduler.Enqueue("DOCUMENT", static (string key, object value, CancellationToken _) => new ValueTask<(string, object)>((key, value)), state);
            Assert.That(second.IsCompleted, Is.False);
            release.SetResult(0);
            var result = await second;
            Assert.That(result.Item1, Is.EqualTo("DOCUMENT"));
            Assert.That(result.Item2, Is.SameAs(state));
            await first;
            await flow.DisposeAsync();
            Assert.That(((PipelineTaskScheduler)scheduler).Coordinator!.KeyCount, Is.Zero);
        }
        finally { release.TrySetResult(0); }
    }

    [Test]
    public async Task ConcurrencyLimit_WorksWithoutKeyedScheduling()
    {
        await using var flow = new TaskFlow();
        var scheduler = flow.WithConcurrencyLimit(2);
        var release = Gate();
        var aStarted = Gate();
        var bStarted = Gate();
        var a = scheduler.Enqueue((Func<Task<int>>)(() => { aStarted.SetResult(0); return release.Task; }));
        var b = scheduler.Enqueue((Func<Task<int>>)(() => { bStarted.SetResult(0); return release.Task; }));
        try
        { await Task.WhenAll(aStarted.Task, bStarted.Task); }
        finally { release.TrySetResult(0); }
        await Task.WhenAll(a, b);
    }

    [Test]
    public void InvalidRoutingAndConfiguration_ThrowSynchronously()
    {
        using var flow = new TaskFlow();
        var scheduler = flow.WithKeyedScheduling<string>();
        Assert.That(() => flow.Enqueue("A", () => 1), Throws.TypeOf<InvalidOperationException>());
        Assert.That(() => scheduler.Enqueue(1, () => 1), Throws.TypeOf<InvalidOperationException>());
        Assert.That(() => scheduler.Enqueue<string>(null!, (Action)(() => { })), Throws.TypeOf<ArgumentNullException>());
        Assert.That(() => scheduler.Enqueue(() => 1), Throws.TypeOf<InvalidOperationException>());
        Assert.That(() => scheduler.WithKeyedScheduling<string>(), Throws.TypeOf<InvalidOperationException>());
        Assert.That(() => flow.WithPerKeyBoundedAdmission(Budget(1)), Throws.TypeOf<InvalidOperationException>());
        Assert.That(() => scheduler.WithConcurrencyLimit(0), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => flow.WithBoundedAdmission(Budget(1)).WithKeyedScheduling<string>(), Throws.TypeOf<InvalidOperationException>());
        using var dedicated = new DedicatedThreadTaskFlow();
        Assert.That(() => dedicated.WithKeyedScheduling<string>(), Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public async Task ConcurrentProducers_FailuresAndCancellation_DoNotOverlapSameKeyOrLeak()
    {
        await using var flow = new TaskFlow();
        var scheduler = flow.WithKeyedScheduling<int>().WithConcurrencyLimit(3)
            .WithBoundedAdmission(new BoundedAdmissionOptions { Capacity = 10 })
            .WithPerKeyBoundedAdmission(new BoundedAdmissionOptions { Capacity = 2 });
        var active = new int[5];
        var total = 0;
        var maximum = 0;
        var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(async () =>
        {
            async Task<int> Execute(int key, CancellationToken token)
            {
                Assert.That(Interlocked.Increment(ref active[key]), Is.EqualTo(1));
                var count = Interlocked.Increment(ref total);
                int old;
                do
                { old = maximum; } while (count > old && Interlocked.CompareExchange(ref maximum, count, old) != old);
                try
                {
                    await Task.Yield();
                    if (i % 13 == 0)
                        throw new FormatException();
                    if (i % 17 == 0)
                        throw new OperationCanceledException(token);
                    return key;
                }
                finally { Interlocked.Decrement(ref total); Interlocked.Decrement(ref active[key]); }
            }
            try
            { await scheduler.Enqueue(i % 5, Execute); }
            catch (FormatException) { }
            catch (OperationCanceledException) { }
        })).ToArray();
        await Task.WhenAll(tasks);
        await flow.DisposeAsync();
        Assert.That(maximum, Is.LessThanOrEqualTo(3));
        Assert.That(active, Is.All.Zero);
        Assert.That(((PipelineTaskScheduler)scheduler).Coordinator!.KeyCount, Is.Zero);
    }

    [Test]
    public async Task Snapshots_ShareExistingBudgets_ButNewRegistrationsAreIndependent()
    {
        await using var flow = new TaskFlow();
        var root = flow.WithKeyedScheduling<string>();
        var admission = Budget(1, TaskSchedulerCapacityMode.Outstanding);
        var bounded = root.WithBoundedAdmission(admission);
        var descendant = bounded.WithOperationName("save");
        var siblingAdmission = Budget(1, TaskSchedulerCapacityMode.Outstanding);
        var sibling = root.WithBoundedAdmission(siblingAdmission);
        var release = Gate();
        var started = Gate();
        var first = Hold(descendant, "A", started, release);
        try
        {
            await started.Task;
            var rejected = admission.Submit(bounded, "B", () => 1, TaskSchedulerAdmissionMode.Reject);
            Assert.That(rejected.Admission.IsCompletedSuccessfully, Is.True);
            Assert.That(await rejected.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Full));
            await Assert.ThatAsync(async () => await rejected.Completion, Throws.TypeOf<AdmissionRejectedException>());
            Assert.That(await siblingAdmission.Submit(sibling, "B", () => 2, TaskSchedulerAdmissionMode.Reject).Completion, Is.EqualTo(2));
        }
        finally { release.TrySetResult(0); }
        await first;
    }

    [Test]
    public async Task SchedulingFailure_ReleasesKeyAndAllReservations()
    {
        var backend = new SwitchableScheduler { Reject = true };
        await using var flow = new TaskFlow(new TaskFlowOptions { TaskScheduler = backend });
        var admission = Budget(1, TaskSchedulerCapacityMode.Outstanding);
        var scheduler = flow.WithKeyedScheduling<string>().WithConcurrencyLimit(1)
            .WithBoundedAdmission(admission).WithPerKeyBoundedAdmission(Budget(1));
        var failed = admission.Submit(scheduler, "A", () => 1, TaskSchedulerAdmissionMode.Wait);
        Assert.That(await failed.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
        await Assert.ThatAsync(async () => await failed.Completion, Throws.TypeOf<TaskSchedulerException>());
        backend.Reject = false;
        Assert.That(await admission.Submit(scheduler, "A", () => 2, TaskSchedulerAdmissionMode.Wait).Completion, Is.EqualTo(2));
        await flow.DisposeAsync();
        Assert.That(((PipelineTaskScheduler)scheduler).Coordinator!.KeyCount, Is.Zero);
    }

    [Test]
    public async Task DisposalRacingAdmissionAndCancellation_SettlesEverySubmission()
    {
        for (var i = 0; i < 30; i++)
        {
            await using var flow = new TaskFlow();
            var admission = Budget(1, TaskSchedulerCapacityMode.Outstanding);
            var scheduler = flow.WithKeyedScheduling<string>().WithConcurrencyLimit(1)
                .WithBoundedAdmission(admission).WithPerKeyBoundedAdmission(Budget(1));
            var release = Gate();
            var started = Gate();
            var first = Hold(scheduler, "A", started, release);
            await started.Task;
            using var cancellation = new CancellationTokenSource();
            var waiting = admission.Submit(scheduler, "A", () => 1, TaskSchedulerAdmissionMode.Wait, cancellation.Token);
            await Task.WhenAll(Task.Run(() => release.TrySetResult(0)), Task.Run(cancellation.Cancel), Task.Run(async () => await flow.DisposeAsync()));
            await first;
            try
            { await waiting.Completion; }
            catch (OperationCanceledException) { }
            catch (AdmissionRejectedException) { }
            Assert.That(waiting.Admission.IsCompleted, Is.True);
            if (waiting.Admission.IsCanceled)
                await Assert.ThatAsync(async () => await waiting.Admission, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(((PipelineTaskScheduler)scheduler).Coordinator!.KeyCount, Is.Zero);
        }
    }

    [Test]
    public async Task DisposalFromKeyedDelegate_IsRejectedWithoutDeadlock()
    {
        await using var flow = new TaskFlow();
        var scheduler = flow.WithKeyedScheduling<string>();
        Task Dispose(string _, CancellationToken token) => flow.DisposeAsync().AsTask();
        await Assert.ThatAsync(async () => await scheduler.Enqueue("A", Dispose), Throws.TypeOf<InvalidOperationException>());
        Assert.That(await scheduler.Enqueue("A", () => 1), Is.EqualTo(1));
    }

    [Test]
    public async Task NativeFactoryRegistration_StillParticipatesInAtomicAdmission()
    {
        await using var flow = new TaskFlow();
        var admission = Budget(1, TaskSchedulerCapacityMode.Outstanding);
        var scheduler = flow.WithKeyedScheduling<string>().WithConcurrencyLimit(1)
            .UseMiddleware(admission).WithPerKeyBoundedAdmission(Budget(1));
        var release = Gate();
        var started = Gate();
        var first = Hold(scheduler, "A", started, release);
        try
        {
            await started.Task;
            var full = admission.Submit(scheduler, "B", () => 1, TaskSchedulerAdmissionMode.Reject);
            Assert.That(await full.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Full));
            await Assert.ThatAsync(async () => await full.Completion, Throws.TypeOf<AdmissionRejectedException>());
        }
        finally { release.TrySetResult(0); }
        await first;
    }

    [Test]
    public async Task CancellationOfAcceptedUndispatchedWork_DoesNotInvokeIt()
    {
        await using var flow = new TaskFlow();
        var admission = Budget(1);
        var scheduler = flow.WithKeyedScheduling<string>().WithConcurrencyLimit(1).WithBoundedAdmission(admission);
        var release = Gate();
        var started = Gate();
        var first = Hold(scheduler, "A", started, release);
        try
        {
            await started.Task;
            using var cancellation = new CancellationTokenSource();
            var queued = admission.Submit(scheduler, "A", (Action)(() => throw new AssertionException("Canceled accepted work invoked")), TaskSchedulerAdmissionMode.Wait, cancellation.Token);
            Assert.That(await queued.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
            await cancellation.CancelAsync();
            await Assert.ThatAsync(async () => await queued.Completion, Throws.InstanceOf<OperationCanceledException>());
            var replacement = admission.Submit(scheduler, "A", () => 2, TaskSchedulerAdmissionMode.Wait);
            Assert.That(await replacement.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
            release.SetResult(0);
            await Task.WhenAll(first, replacement.Completion);
        }
        finally { release.TrySetResult(0); }
    }

    [Test]
    public async Task Timeout_RetainsExecutionSlotForOtherKeysWithoutAdmissionBounds()
    {
        var backend = new SwitchableScheduler();
        await using var flow = new TaskFlow(new TaskFlowOptions { TaskScheduler = backend });
        var core = flow.WithKeyedScheduling<string>().WithConcurrencyLimit(1);
        var release = Gate();
        var started = Gate();
        var first = Hold(core.WithTimeout(TimeSpan.FromMilliseconds(100)), "A", started, release);
        try
        {
            await started.Task;
            await Assert.ThatAsync(async () => await first, Throws.TypeOf<TimeoutException>());
            var before = backend.ScheduledCount;
            var other = core.Enqueue("B", () => 2);
            Assert.That(backend.ScheduledCount, Is.EqualTo(before));
            release.SetResult(0);
            Assert.That(await other, Is.EqualTo(2));
        }
        finally { release.TrySetResult(0); }
    }

    [Test]
    public async Task QueuedWork_PreservesSubmittingExecutionContextAndConfiguredScheduler()
    {
        var backend = new SwitchableScheduler();
        await using var flow = new TaskFlow(new TaskFlowOptions { TaskScheduler = backend });
        var scheduler = flow.WithKeyedScheduling<string>();
        var ambient = new AsyncLocal<string?> { Value = "first" };
        var release = Gate();
        var started = Gate();
        var first = Hold(scheduler, "A", started, release);
        try
        {
            await started.Task;
            ambient.Value = "second";
            async Task<(string?, TaskScheduler)> Read(string key, CancellationToken token)
            {
                await Task.Yield();
                return (ambient.Value, TaskScheduler.Current);
            }
            var second = scheduler.Enqueue("A", Read);
            ambient.Value = "releaser";
            release.SetResult(0);
            var result = await second;
            Assert.That(result.Item1, Is.EqualTo("second"));
            Assert.That(result.Item2, Is.SameAs(backend));
            await first;
        }
        finally { release.TrySetResult(0); }
    }

    [Test]
    public async Task SuppressedContext_DoesNotInheritPredecessor_AndStillGuardsSelfDisposal()
    {
        await using var flow = new TaskFlow();
        var scheduler = flow.WithKeyedScheduling<string>();
        var ambient = new AsyncLocal<string?> { Value = "predecessor" };
        var release = Gate();
        var started = Gate();
        var first = Hold(scheduler, "A", started, release);
        try
        {
            await started.Task;
            async Task<string?> Probe(string key, CancellationToken token)
            {
                var value = ambient.Value;
                await Assert.ThatAsync(async () => await flow.DisposeAsync(), Throws.TypeOf<InvalidOperationException>());
                return value;
            }
            Task<string?> second;
            using (ExecutionContext.SuppressFlow())
                second = scheduler.Enqueue("A", Probe);
            release.SetResult(0);
            Assert.That(await second, Is.Null);
            await first;
        }
        finally { release.TrySetResult(0); }
    }

    [Test]
    public async Task StructKeys_StayTyped_AndSharePerKeyAdmissionByTypedEquality()
    {
        await using var flow = new TaskFlow();
        var admission = Budget(1);
        var objectEqualityCalls = RoutingKey.ObjectEqualityCalls;
        var observer = new TypedKeyObserver<RoutingKey>();
        var scheduler = flow.WithKeyedScheduling<RoutingKey>().WithConcurrencyLimit(2)
            .WithPerKeyBoundedAdmission(admission).UseMiddleware(observer);
        Assert.That(((PipelineTaskScheduler)scheduler).Coordinator is KeyedCoordinator<RoutingKey>, Is.True);
        var release = Gate();
        var started = Gate();
        Task<int> HoldKey(RoutingKey key, CancellationToken token)
        {
            started.TrySetResult(0);
            return release.Task;
        }
        var first = scheduler.Enqueue(new RoutingKey(1, 10), HoldKey);
        try
        {
            await started.Task;
            var queued = admission.Submit(scheduler, new RoutingKey(1, 20), (Func<RoutingKey, int>)(key => key.Original), TaskSchedulerAdmissionMode.Reject);
            Assert.That(await queued.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
            Assert.That(queued.Completion.IsCompleted, Is.False);
            var full = admission.Submit(scheduler, new RoutingKey(1, 30), () => 30, TaskSchedulerAdmissionMode.Reject);
            Assert.That(await full.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Full));
            await Assert.ThatAsync(async () => await full.Completion, Throws.TypeOf<AdmissionRejectedException>());
            var other = admission.Submit(scheduler, new RoutingKey(2, 40), (Func<RoutingKey, int>)(key => key.Original), TaskSchedulerAdmissionMode.Reject);
            Assert.That(await other.Completion, Is.EqualTo(40));
            release.SetResult(10);
            Assert.That(await queued.Completion, Is.EqualTo(20));
            await first;
            Assert.That(observer.Seen.Select(key => key.Original), Is.EqualTo(StructKeyOrder));
            Assert.That(RoutingKey.ObjectEqualityCalls, Is.EqualTo(objectEqualityCalls));
        }
        finally { release.TrySetResult(0); }
        await flow.DisposeAsync();
        Assert.That(((PipelineTaskScheduler)scheduler).Coordinator!.KeyCount, Is.Zero);
    }

    [Test]
    public async Task GenericSubmission_RoundTripsIntGuidAndEnumKeys()
    {
        await Verify(42);
        await Verify(Guid.Parse("11c57948-8696-4c49-966f-6f6d4381d89e"));
        await Verify(TaskSchedulerCapacityMode.Pending);

        static async Task Verify<TKey>(TKey key) where TKey : notnull
        {
            await using var flow = new TaskFlow();
            var admission = Budget(2);
            var observer = new TypedKeyObserver<TKey>();
            var scheduler = flow.WithKeyedScheduling<TKey>().WithBoundedAdmission(admission).UseMiddleware(observer);
            Func<TKey, TKey> identity = static value => value;
            var enqueued = await scheduler.Enqueue(key, identity);
            var submitted = admission.Submit(scheduler, key, identity, TaskSchedulerAdmissionMode.Reject);
            Assert.That(await submitted.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
            Assert.That(EqualityComparer<TKey>.Default.Equals(enqueued, key), Is.True);
            Assert.That(EqualityComparer<TKey>.Default.Equals(await submitted.Completion, key), Is.True);
            Assert.That(observer.Seen.Count, Is.EqualTo(2));
            Assert.That(((PipelineTaskScheduler)scheduler).Coordinator is IKeyedCoordinator<TKey>, Is.True);
        }
    }

    private readonly struct RoutingKey(int lane, int original) : IEquatable<RoutingKey>
    {
        private static int _objectEqualityCalls;

        public static int ObjectEqualityCalls => Volatile.Read(ref _objectEqualityCalls);
        public int Original { get; } = original;
        private int Lane { get; } = lane;
        public bool Equals(RoutingKey other) => Lane == other.Lane;
        public override bool Equals(object? obj)
        {
            Interlocked.Increment(ref _objectEqualityCalls);
            return obj is RoutingKey other && Equals(other);
        }
        public override int GetHashCode() => Lane;
    }

    private sealed class TypedKeyObserver<TKey> : ITaskSchedulerEnqueueMiddleware where TKey : notnull
    {
        public List<TKey> Seen { get; } = new();
        public Task<T> InvokeAsync<T>(TaskSchedulerEnqueueContext<T> context, TaskSchedulerEnqueueDelegate<T> next)
        {
            if (context.Operation is not KeyedPipelineOperation<TKey, T> operation)
                throw new AssertionException("The pipeline must retain a reference envelope with a typed key field.");
            Seen.Add(operation.Key);
            return next(context);
        }
    }

    private sealed class SwitchableScheduler : TaskScheduler
    {
        private int _scheduled;
        public bool Reject { get; set; }
        public int ScheduledCount => Volatile.Read(ref _scheduled);
        protected override IEnumerable<Task> GetScheduledTasks() => Array.Empty<Task>();
        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;
        protected override void QueueTask(Task task)
        {
            if (Reject)
                throw new InvalidOperationException("Scheduling refused");
            Interlocked.Increment(ref _scheduled);
            _ = Task.Run(() => TryExecuteTask(task));
        }
    }

    private sealed class ExecutionDelay(TaskCompletionSource<int> entered, TaskCompletionSource<int> release) : ITaskSchedulerExecutionMiddleware
    {
        public async ValueTask<T> InvokeAsync<T>(TaskSchedulerOperationContext context, TaskSchedulerExecutionDelegate<T> next)
        {
            entered.TrySetResult(0);
            await release.Task;
            return await next(context);
        }
    }
}