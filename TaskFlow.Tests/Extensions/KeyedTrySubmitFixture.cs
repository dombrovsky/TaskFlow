namespace TaskFlow.Tests.Extensions;

using NUnit.Framework;
using System.Threading.Tasks.Flow;
using System.Threading.Tasks.Flow.Internal;

[CancelAfter(15000)]
internal sealed class KeyedTrySubmitFixture
{
    private static readonly string[] ExpectedOrder = ["A1", "B1", "C1", "A2", "B2", "A3"];
    private static TaskCompletionSource<int> Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static BoundedAdmissionController Budget(int capacity, TaskSchedulerCapacityMode mode = TaskSchedulerCapacityMode.Outstanding)
        => new(new BoundedAdmissionOptions { Capacity = capacity, CapacityMode = mode, MaxAdmissionWaiters = 1 });

    [TestCase(TaskSchedulerCapacityMode.Pending)]
    [TestCase(TaskSchedulerCapacityMode.Outstanding)]
    public async Task CombinedBudgets_RejectWithoutPartialReservations_AndAllowUnrelatedKeys(TaskSchedulerCapacityMode mode)
    {
        await using var flow = new TaskFlow();
        var global = Budget(2, mode);
        var perKey = Budget(1, mode);
        var scheduler = flow.WithKeyedScheduling<string>().WithConcurrencyLimit(1)
            .WithBoundedAdmission(global).WithPerKeyBoundedAdmission(perKey);
        var release = Gate();
        var started = Gate();
        var first = scheduler.Enqueue("A", (string _, CancellationToken _) => { started.SetResult(0); return release.Task; });
        try
        {
            await started.Task;
            Task? pendingCompletion = null;
            if (mode == TaskSchedulerCapacityMode.Pending)
            {
                var pending = global.TrySubmit(scheduler, "A", key => key);
                Assert.That(pending.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
                pendingCompletion = pending.Completion;
            }
            Assert.That(global.TrySubmit(scheduler, "A", _ => 0).Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Full));
            var waiting = global.Submit(scheduler, "A", _ => 2, TaskSchedulerAdmissionMode.Wait);
            Assert.That(waiting.Admission.IsCompleted, Is.False);
            // A's full per-key budget must not reserve the remaining global budget or execution slot.
            var other = perKey.TrySubmit(scheduler, "B", key => key);
            Assert.That(other.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
            Assert.That(other.Completion!.IsCompleted, Is.False);
            Assert.That(perKey.TrySubmit(scheduler, "C", _ => 0).Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Full));
            release.SetResult(1);
            await first;
            if (pendingCompletion != null)
                await pendingCompletion;
            Assert.That(await other.Completion, Is.EqualTo("B"));
            Assert.That(await waiting.Completion, Is.EqualTo(2));
            Assert.That(await global.TrySubmit(scheduler, "C", _ => 3).Completion!, Is.EqualTo(3));
        }
        finally { release.TrySetResult(0); }
        await flow.DisposeAsync();
        Assert.That(((PipelineTaskScheduler)scheduler).Coordinator!.KeyCount, Is.Zero);
    }

    [Test]
    public async Task ConcurrentProducers_ReserveEveryBudgetAtomically()
    {
        await using var flow = new TaskFlow();
        var global = Budget(6);
        var perKey = Budget(2);
        var scheduler = flow.WithKeyedScheduling<int>().WithConcurrencyLimit(2)
            .WithBoundedAdmission(global).WithPerKeyBoundedAdmission(perKey);
        var release = Gate();
        var start = Gate();
        var active = new int[4];
        var total = 0;
        var twoStarted = Gate();
        async Task<int> Hold(int key, CancellationToken token)
        {
            Assert.That(Interlocked.Increment(ref active[key]), Is.EqualTo(1));
            var count = Interlocked.Increment(ref total);
            Assert.That(count, Is.LessThanOrEqualTo(2));
            if (count == 2)
                twoStarted.TrySetResult(0);
            try
            { return await release.Task; }
            finally { Interlocked.Decrement(ref active[key]); Interlocked.Decrement(ref total); }
        }
        var producers = Enumerable.Range(0, 100).Select(i => Task.Run(async () =>
        {
            await start.Task;
            return (key: i % 4, submission: global.TrySubmit(scheduler, i % 4, Hold));
        })).ToArray();
        try
        {
            start.SetResult(0);
            var results = await Task.WhenAll(producers);
            var accepted = results.Where(r => r.submission.Admission == TaskSchedulerAdmissionResult.Accepted).ToArray();
            Assert.That(accepted, Has.Length.EqualTo(6));
            Assert.That(accepted.GroupBy(r => r.key).Select(g => g.Count()), Is.All.LessThanOrEqualTo(2));
            await twoStarted.Task;
            release.SetResult(42);
            Assert.That(await Task.WhenAll(accepted.Select(r => r.submission.Completion!)), Is.All.EqualTo(42));
            // A rejected key must not have retained a partial per-key reservation.
            foreach (var key in Enumerable.Range(0, 4))
                Assert.That(await perKey.TrySubmit(scheduler, key, k => k).Completion!, Is.EqualTo(key));
        }
        finally { start.TrySetResult(0); release.TrySetResult(0); }
        await flow.DisposeAsync();
        Assert.That(((PipelineTaskScheduler)scheduler).Coordinator!.KeyCount, Is.Zero);
    }

    [Test]
    public async Task MixedTrySubmitSubmitAndEnqueue_PreserveFifoRoundRobinAndSameKeyExclusion()
    {
        await using var flow = new TaskFlow();
        var admission = Budget(10);
        var scheduler = flow.WithKeyedScheduling<string>().WithConcurrencyLimit(1).WithBoundedAdmission(admission);
        var release = Gate();
        var started = Gate();
        var blocker = scheduler.Enqueue("blocker", (string _, CancellationToken _) => { started.SetResult(0); return release.Task; });
        var order = new List<string>();
        var tasks = new List<Task>();
        try
        {
            await started.Task;
            Task Record(string key, string label)
            {
                var submitted = admission.TrySubmit(scheduler, key, _ => order.Add(label));
                Assert.That(submitted.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
                return submitted.Completion!;
            }
            tasks.Add(Record("A", "A1"));
            tasks.Add(scheduler.Enqueue("A", () => order.Add("A2")));
            tasks.Add(admission.Submit(scheduler, "A", () => order.Add("A3"), TaskSchedulerAdmissionMode.Wait).Completion);
            tasks.Add(Record("B", "B1"));
            tasks.Add(Record("B", "B2"));
            tasks.Add(Record("C", "C1"));
            release.SetResult(0);
            await Task.WhenAll(tasks.Append(blocker));
            Assert.That(order, Is.EqualTo(ExpectedOrder));
        }
        finally { release.TrySetResult(0); }
    }

    [Test]
    public async Task EligibleWaiter_CannotBeOvertakenByTrySubmit()
    {
        await using var flow = new TaskFlow();
        var admission = Budget(1);
        var scheduler = flow.WithKeyedScheduling<string>().WithBoundedAdmission(admission);
        var release = Gate();
        var waiterRelease = Gate();
        var started = Gate();
        var first = scheduler.Enqueue("A", (string _, CancellationToken _) => { started.SetResult(0); return release.Task; });
        try
        {
            await started.Task;
            Task<int> Hold(string key, CancellationToken token) => waiterRelease.Task;
            var waiting = admission.Submit(scheduler, "B", Hold, TaskSchedulerAdmissionMode.Wait);
            Assert.That(waiting.Admission.IsCompleted, Is.False);
            Assert.That(admission.TrySubmit(scheduler, "C", _ => 3).Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Full));
            release.SetResult(1);
            Assert.That(await waiting.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
            Assert.That(admission.TrySubmit(scheduler, "C", _ => 3).Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Full));
            waiterRelease.SetResult(2);
            await Task.WhenAll(first, waiting.Completion);
        }
        finally { release.TrySetResult(0); waiterRelease.TrySetResult(0); }
    }

    [Test]
    public async Task CancellationOfAcceptedUndispatchedWork_ReleasesAllBudgetsWithoutInvocation()
    {
        await using var flow = new TaskFlow();
        var global = Budget(2);
        var perKey = Budget(2);
        var scheduler = flow.WithKeyedScheduling<string>().WithConcurrencyLimit(1)
            .WithBoundedAdmission(global).WithPerKeyBoundedAdmission(perKey);
        var release = Gate();
        var started = Gate();
        var first = scheduler.Enqueue("A", (string _, CancellationToken _) => { started.SetResult(0); return release.Task; });
        try
        {
            await started.Task;
            using var cancellation = new CancellationTokenSource();
            var queued = global.TrySubmit(scheduler, "A", (Action<string>)(_ => throw new AssertionException("Canceled work executed")), cancellation.Token);
            Assert.That(queued.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
            await cancellation.CancelAsync();
            await Assert.ThatAsync(async () => await queued.Completion!, Throws.InstanceOf<OperationCanceledException>());
            var replacement = global.TrySubmit(scheduler, "A", _ => 2);
            Assert.That(replacement.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
            release.SetResult(1);
            await first;
            Assert.That(await replacement.Completion!, Is.EqualTo(2));
        }
        finally { release.TrySetResult(0); }
    }

    [Test]
    public async Task Shutdown_CancelsPendingAcceptedWork_AndReturnsClosedForNewWork()
    {
        await using var flow = new TaskFlow();
        var admission = Budget(2);
        var scheduler = flow.WithKeyedScheduling<string>().WithConcurrencyLimit(1).WithBoundedAdmission(admission);
        var release = Gate();
        var started = Gate();
        var first = scheduler.Enqueue("A", (string _, CancellationToken _) => { started.SetResult(0); return release.Task; });
        try
        {
            await started.Task;
            var pending = admission.TrySubmit(scheduler, "B", (Action<string>)(_ => throw new AssertionException("Shutdown work executed")));
            Assert.That(pending.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
            var shutdown = flow.DisposeAsync().AsTask();
            var closed = admission.TrySubmit(scheduler, "C", _ => 3);
            Assert.That(closed.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Closed));
            Assert.That(closed.Completion, Is.Null);
            await Assert.ThatAsync(async () => await pending.Completion!, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(shutdown.IsCompleted, Is.False);
            release.SetResult(1);
            await Task.WhenAll(first, shutdown);
            Assert.That(((PipelineTaskScheduler)scheduler).Coordinator!.KeyCount, Is.Zero);
        }
        finally { release.TrySetResult(0); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SubmissionRacingDisposalAndCancellation_SettlesAllWork(bool keyed)
    {
        for (var iteration = 0; iteration < 30; iteration++)
        {
            await using var flow = new TaskFlow();
            var admission = Budget(2);
            var scheduler = (keyed ? flow.WithKeyedScheduling<string>().WithConcurrencyLimit(1) : (ITaskScheduler)flow).WithBoundedAdmission(admission);
            using var cancellation = new CancellationTokenSource();
            var start = Gate();
            var submit = Task.Run(async () =>
            {
                await start.Task;
                try
                {
                    var result = keyed ? admission.TrySubmit(scheduler, "A", _ => 1, cancellation.Token)
                        : admission.TrySubmit(scheduler, () => 1, cancellation.Token);
                    Assert.That(result.Admission, Is.AnyOf(TaskSchedulerAdmissionResult.Accepted, TaskSchedulerAdmissionResult.Closed));
                    if (result.Completion != null)
                    {
                        try
                        { await result.Completion; }
                        catch (OperationCanceledException) { }
                        catch (ObjectDisposedException) { }
                    }
                }
                catch (OperationCanceledException) { }
            });
            var dispose = Task.Run(async () => { await start.Task; await flow.DisposeAsync(); });
            var cancel = Task.Run(async () => { await start.Task; await cancellation.CancelAsync(); });
            start.SetResult(0);
            await Task.WhenAll(submit, dispose, cancel);
            if (keyed)
                Assert.That(((PipelineTaskScheduler)scheduler).Coordinator!.KeyCount, Is.Zero);
        }
    }

    [Test]
    public async Task TypedKeyStateAndAsyncOverloads_RetainContextAndExposeFailure()
    {
        await using var flow = new TaskFlow();
        var admission = Budget(1);
        var scheduler = flow.WithKeyedScheduling<Guid>().WithBoundedAdmission(admission);
        var key = Guid.NewGuid();
        var state = new object();
        using var cancellation = new CancellationTokenSource();
        var result = admission.TrySubmit(scheduler, key, (Guid k, object s, CancellationToken token) =>
        {
            Assert.That(k, Is.EqualTo(key));
            Assert.That(s, Is.SameAs(state));
            Assert.That(token.CanBeCanceled, Is.True);
            return new ValueTask<Guid>(k);
        }, state, cancellation.Token);
        Assert.That(await result.Completion!, Is.EqualTo(key));
        Task<Guid> TaskResult(Guid k, CancellationToken _) => Task.FromResult(k);
        ValueTask<Guid> ValueResult(Guid k, CancellationToken _) => new(k);
        Task TaskAction(Guid key, CancellationToken token) => Task.CompletedTask;
        ValueTask ValueAction(Guid key, CancellationToken token) => default;
        Assert.That(await admission.TrySubmit(scheduler, key, TaskResult).Completion!, Is.EqualTo(key));
        Assert.That(await admission.TrySubmit(scheduler, key, ValueResult, cancellation.Token).Completion!, Is.EqualTo(key));
        await admission.TrySubmit(scheduler, key, TaskAction, cancellation.Token).Completion!;
        await admission.TrySubmit(scheduler, key, ValueAction).Completion!;
        await admission.TrySubmit(scheduler, key, static (Guid _, object _, CancellationToken _) => default(ValueTask), state).Completion!;
        var failed = admission.TrySubmit(scheduler, key, (Func<Guid, int>)(_ => throw new FormatException()));
        Assert.That(failed.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
        await Assert.ThatAsync(async () => await failed.Completion!, Throws.TypeOf<FormatException>());
        Assert.That(() => admission.TrySubmit(scheduler, () => 1), Throws.TypeOf<InvalidOperationException>());
        Assert.That(() => admission.TrySubmit(scheduler, 1, _ => 1), Throws.TypeOf<InvalidOperationException>());
        await cancellation.CancelAsync();
        Assert.That(() => admission.TrySubmit(scheduler, key, _ => 0, cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
    }
}