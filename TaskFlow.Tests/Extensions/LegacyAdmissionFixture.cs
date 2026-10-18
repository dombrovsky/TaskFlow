namespace TaskFlow.Tests.Extensions;

using NUnit.Framework;
using System.Threading.Tasks.Flow;

[CancelAfter(15000)]
internal sealed class LegacyAdmissionFixture
{
    [ThreadStatic]
    private static bool _submitting;

    private static ITaskScheduler Configure(int kind, TaskFlow flow, TaskScheduler backend, BoundedAdmissionController budget)
        => (kind switch
        {
            1 => flow.WithKeyedScheduling<string>().WithConcurrencyLimit(1),
            2 => flow.WithConcurrencyLimit(1),
            _ => new TaskFlowSchedulerAdapter(backend)
        }).WithBoundedAdmission(budget, flow.MiddlewareLifetime);

    [TestCase(0, false)]
    [TestCase(0, true)]
    [TestCase(1, false)]
    [TestCase(1, true)]
    [TestCase(2, false)]
    [TestCase(2, true)]
    public async Task LegacyOnlySuppressedSubmission_KeepsImmediateInlineSchedulerCallbacks(int kind, bool submit)
    {
        var ambient = new AsyncLocal<string?> { Value = "producer" };
        var calledInline = false;
        var backend = new InlineBackend(() =>
        {
            calledInline = _submitting;
            Assert.That(ambient.Value, Is.EqualTo("producer"));
        });
        await using var cleanup = new AdmissionTestOwner<TaskFlow>(() => new TaskFlow(new TaskFlowOptions { TaskScheduler = backend }));
        var flow = cleanup.Value;
        var budget = new BoundedAdmissionController(new BoundedAdmissionOptions { Capacity = 1 });
        var scheduler = Configure(kind, flow, backend, budget);
        Func<string?> read = () => ambient.Value;
        Func<string, string?> keyedRead = _ => ambient.Value;
        Task<string?> completion;
        _submitting = true;
        try
        {
            using var suppression = ExecutionContext.SuppressFlow();
            if (submit)
            {
                var attempt = kind == 1 ? budget.Submit(scheduler, "A", keyedRead, TaskSchedulerAdmissionMode.Reject)
                    : budget.Submit(scheduler, read, TaskSchedulerAdmissionMode.Reject);
                Assert.That(attempt.Admission.IsCompletedSuccessfully, Is.True);
                completion = attempt.Completion;
            }
            else
                completion = kind == 1 ? scheduler.Enqueue("A", keyedRead) : scheduler.Enqueue(read);
        }
        finally { _submitting = false; }
        Assert.That(calledInline, Is.True);
        Assert.That(await completion.WaitForTestAsync(), Is.EqualTo("producer"));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task LegacyOnlyWaiters_KeepBackpressureOrderingAndContextSemantics(int kind)
    {
        await using var cleanup = new AdmissionTestOwner<TaskFlow>(() => new TaskFlow());
        var flow = cleanup.Value;
        var budget = new BoundedAdmissionController(new BoundedAdmissionOptions { Capacity = 1 });
        var scheduler = Configure(kind, flow, TaskScheduler.Default, budget);
        var ambient = new AsyncLocal<string?>();
        var entered = new AdmissionTestGate();
        var release = new AdmissionTestGate();
        var order = new List<int>();
        Task<string?> Enqueue(Func<CancellationToken, Task<string?>> action) => kind == 1 ? scheduler.Enqueue("A", action) : scheduler.Enqueue(action);
        async Task<string?> Hold(CancellationToken _)
        {
            order.Add(1);
            var value = ambient.Value;
            entered.TrySetResult(0);
            await release.Task;
            return value;
        }
        ambient.Value = "first";
        var first = Enqueue(Hold);
        try
        {
            await entered.Task.WaitForTestAsync();
            ambient.Value = "second";
            Task<string?> Second(CancellationToken _)
            { order.Add(2); return Task.FromResult<string?>(ambient.Value); }
            var waiting = kind == 1 ? budget.Submit(scheduler, "A", Second, TaskSchedulerAdmissionMode.Wait)
                : budget.Submit(scheduler, Second, TaskSchedulerAdmissionMode.Wait);
            Assert.That(waiting.Admission.IsCompleted, Is.False);
            ambient.Value = "third";
            Task<string?> Third(CancellationToken _)
            { order.Add(3); return Task.FromResult<string?>(ambient.Value); }
            Task<string?> third;
            using (ExecutionContext.SuppressFlow())
                third = Enqueue(Third);
            var rejected = kind == 1 ? budget.Submit(scheduler, "A", _ => "rejected", TaskSchedulerAdmissionMode.Reject)
                : budget.Submit(scheduler, () => "rejected", TaskSchedulerAdmissionMode.Reject);
            Assert.That(await rejected.Admission.WaitForTestAsync(), Is.EqualTo(TaskSchedulerAdmissionResult.Full));
            await Assert.ThatAsync(async () => await rejected.Completion.WaitForTestAsync(), Throws.TypeOf<AdmissionRejectedException>());
            release.TrySetResult(0);
            var values = await Task.WhenAll(first, waiting.Completion, third).WaitForTestAsync();
            Assert.That(order, Is.EqualTo(Enumerable.Range(1, 3)));
            // The uncoordinated gate historically invokes waiters in each releaser's context.
            // Coordinated legacy submissions capture their own producer contexts before waiting.
            var expected = kind == 0 ? new string?[] { "first", "first", "second" } : ["first", "second", null];
            Assert.That(values, Is.EqualTo(expected));
            Assert.That(await waiting.Admission.WaitForTestAsync(), Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
        }
        finally { release.TrySetResult(0); }
    }

    [TestCase(0, false)]
    [TestCase(0, true)]
    [TestCase(1, false)]
    [TestCase(1, true)]
    [TestCase(2, false)]
    [TestCase(2, true)]
    public async Task SuppressedLegacyExecution_RetainsTheSelfDisposalGuard(int kind, bool submit)
    {
        await using var owner = new AdmissionTestOwner<TaskFlow>(() => new TaskFlow());
        var flow = owner.Value;
        var budget = new BoundedAdmissionController(new BoundedAdmissionOptions { Capacity = 1 });
        var scheduler = Configure(kind, flow, TaskScheduler.Default, budget);
        Task SelfDispose(CancellationToken _) => flow.DisposeAsync().AsTask();
        Task completion;
        using (ExecutionContext.SuppressFlow())
        {
            if (submit)
                completion = kind == 1 ? budget.Submit(scheduler, "A", SelfDispose, TaskSchedulerAdmissionMode.Reject).Completion
                    : budget.Submit(scheduler, SelfDispose, TaskSchedulerAdmissionMode.Reject).Completion;
            else
                completion = kind == 1 ? scheduler.Enqueue("A", SelfDispose) : scheduler.Enqueue(SelfDispose);
        }
        await Assert.ThatAsync(async () => await completion.WaitForTestAsync(), Throws.TypeOf<InvalidOperationException>());
    }

    private sealed class InlineBackend(Action queued) : TaskScheduler
    {
        protected override IEnumerable<Task> GetScheduledTasks() => Array.Empty<Task>();
        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => TryExecuteTask(task);
        protected override void QueueTask(Task task) { queued(); TryExecuteTask(task); }
    }
}