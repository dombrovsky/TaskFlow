namespace TaskFlow.Tests.Extensions;

using NUnit.Framework;
using System.Threading.Tasks.Flow;

[CancelAfter(15000)]
internal sealed class AdmissionContextFixture
{
    [TestCase(0, false)]
    [TestCase(0, true)]
    [TestCase(1, false)]
    [TestCase(1, true)]
    [TestCase(2, false)]
    [TestCase(2, true)]
    public async Task TerminalContextSemantics_AreTheSameForEnqueueAndTrySubmit(int terminal, bool synchronous)
    {
        var ambient = new AsyncLocal<string?> { Value = "terminal" };
        var owner = new AdmissionTestOwner<TaskFlowBase>(() => terminal switch { 1 => new DedicatedThreadTaskFlow(), 2 => new CurrentThreadTaskFlow(), _ => new TaskFlow() });
        var flow = owner.Value;
        Thread? runner = null;
        if (flow is CurrentThreadTaskFlow current)
        {
            runner = new Thread(() => { ambient.Value = "terminal"; current.Run(); }) { IsBackground = true };
            runner.Start();
        }
        var budget = new BoundedAdmissionController(new BoundedAdmissionOptions { Capacity = 4 });
        var scheduler = flow.WithBoundedAdmission(budget);
        var entered = new AdmissionTestGate();
        var release = new AdmissionTestGate();
        Task Hold(CancellationToken _)
        { entered.TrySetResult(0); return release.Task; }
        var blocker = scheduler.Enqueue(Hold);
        var completions = new List<Task<(string? Before, string? After)>>();
        async Task<(string?, string?)> Read(CancellationToken _)
        {
            var before = ambient.Value;
            if (flow is ThreadTaskFlow affine)
                Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(affine.ThreadId));
            await Task.Yield();
            if (flow is ThreadTaskFlow after)
                Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(after.ThreadId));
            return (before, ambient.Value);
        }
        Task<(string?, string?)> Submit() => synchronous ? budget.TrySubmit(scheduler, Read).Completion! : scheduler.Enqueue(Read);
        try
        {
            await entered.Task.WaitForTestAsync();
            ambient.Value = "first";
            completions.Add(Submit());
            ambient.Value = "second";
            completions.Add(Submit());
            using (ExecutionContext.SuppressFlow())
                completions.Add(Submit());
            release.TrySetResult(0);
            await blocker.WaitForTestAsync();
            var results = await Task.WhenAll(completions).WaitForTestAsync();
            var expected = terminal == 0 ? new string?[] { "first", "second", null } : ["terminal", "terminal", "terminal"];
            Assert.That(results.Select(r => r.Before), Is.EqualTo(expected));
            Assert.That(results.Select(r => r.After), Is.EqualTo(expected));
        }
        finally
        {
            release.TrySetResult(0);
            await owner.DisposeAsync();
            if (runner != null)
                Assert.That(runner.Join(AdmissionTestTimeouts.Timeout), Is.True, "Run did not finish after disposal");
        }
    }
}