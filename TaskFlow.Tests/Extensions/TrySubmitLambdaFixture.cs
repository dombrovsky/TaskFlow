namespace TaskFlow.Tests.Extensions;

using NUnit.Framework;
using System.Threading.Tasks.Flow;

[CancelAfter(15000)]
internal sealed class TrySubmitLambdaFixture
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task OrdinaryAsyncLambdas_CompileAndAwaitResults_WithAndWithoutTokens(bool keyed)
    {
        await using var cleanup = new AdmissionTestOwner<TaskFlow>(() => new TaskFlow());
        var flow = cleanup.Value;
        var budget = new BoundedAdmissionController(new BoundedAdmissionOptions { Capacity = 1 });
        var scheduler = (keyed ? flow.WithKeyedScheduling<string>() : (ITaskScheduler)flow).WithBoundedAdmission(budget);
        var token = TestContext.CurrentContext.CancellationToken;
        for (var shape = 0; shape < 8; shape++)
        {
            var gate = new AdmissionTestGate();
            TaskSchedulerTrySubmission attempt = (keyed, shape) switch
            {
                (false, 0) => budget.TrySubmit(scheduler, async () => { await gate.Task; }),
                (false, 1) => budget.TrySubmit(scheduler, async () => { await gate.Task; return 42; }),
                (false, 2) => budget.TrySubmit(scheduler, async ct => { await gate.Task; }),
                (false, 3) => budget.TrySubmit(scheduler, async ct => { await gate.Task; return 42; }),
                (false, 4) => budget.TrySubmit(scheduler, async () => { await gate.Task; }, token),
                (false, 5) => budget.TrySubmit(scheduler, async () => { await gate.Task; return 42; }, token),
                (false, 6) => budget.TrySubmit(scheduler, async ct => { await gate.Task; }, token),
                (false, 7) => budget.TrySubmit(scheduler, async ct => { await gate.Task; return 42; }, token),
                (true, 0) => budget.TrySubmit(scheduler, "A", async key => { await gate.Task; }),
                (true, 1) => budget.TrySubmit(scheduler, "A", async key => { await gate.Task; return 42; }),
                (true, 2) => budget.TrySubmit(scheduler, "A", async (key, ct) => { await gate.Task; }),
                (true, 3) => budget.TrySubmit(scheduler, "A", async (key, ct) => { await gate.Task; return 42; }),
                (true, 4) => budget.TrySubmit(scheduler, "A", async key => { await gate.Task; }, token),
                (true, 5) => budget.TrySubmit(scheduler, "A", async key => { await gate.Task; return 42; }, token),
                (true, 6) => budget.TrySubmit(scheduler, "A", async (key, ct) => { await gate.Task; }, token),
                (true, 7) => budget.TrySubmit(scheduler, "A", async (key, ct) => { await gate.Task; return 42; }, token),
                _ => throw new AssertionException("Unknown delegate shape")
            };
            try
            {
                Assert.That(attempt.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
                Assert.That(attempt.Completion!.IsCompleted, Is.False);
                gate.SetResult(0);
                await attempt.Completion.WaitForTestAsync();
                if ((shape & 1) == 1)
                {
                    Assert.That(attempt, Is.InstanceOf<TaskSchedulerTrySubmission<int>>());
                    Task<int> result = ((TaskSchedulerTrySubmission<int>)attempt).Completion!;
                    Assert.That(await result.WaitForTestAsync(), Is.EqualTo(42));
                }
            }
            finally { gate.TrySetResult(0); }
        }
        // Typed-state async lambdas have one ValueTask target; their result is never a nested task.
        var state = new object();
        TaskSchedulerTrySubmission<int> typed = keyed
            ? budget.TrySubmit(scheduler, "A", async (string key, object value, CancellationToken ct) => { await Task.Yield(); Assert.That(value, Is.SameAs(state)); return 42; }, state, token)
            : budget.TrySubmit(scheduler, async (object value, CancellationToken ct) => { await Task.Yield(); Assert.That(value, Is.SameAs(state)); return 42; }, state, token);
        Assert.That(await typed.Completion!.WaitForTestAsync(), Is.EqualTo(42));

        var actionState = new int[1];
        TaskSchedulerTrySubmission action = keyed
            ? budget.TrySubmit(scheduler, "A", async (string key, int[] value, CancellationToken ct) => { await Task.Yield(); value[0] = 42; }, actionState)
            : budget.TrySubmit(scheduler, async (int[] value, CancellationToken ct) => { await Task.Yield(); value[0] = 42; }, actionState);
        await action.Completion!.WaitForTestAsync();
        Assert.That(actionState[0], Is.EqualTo(42));
    }
}