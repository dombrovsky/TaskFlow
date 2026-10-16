namespace TaskFlow.Tests.Extensions;

using NUnit.Framework;
using System.Threading.Tasks.Flow;
using System.Threading.Tasks.Flow.Internal;

[TestFixture]
[CancelAfter(10000)]
internal sealed class AdmissionRequestFixture
{
    private static AdmissionRequest Request() => new(
        new BoundedAdmissionController(new BoundedAdmissionOptions { Capacity = 1 }),
        TaskSchedulerAdmissionMode.Wait);

    [TestCase(TaskSchedulerAdmissionResult.Accepted)]
    [TestCase(TaskSchedulerAdmissionResult.Full)]
    [TestCase(TaskSchedulerAdmissionResult.WaiterLimitReached)]
    [TestCase(TaskSchedulerAdmissionResult.Closed)]
    public async Task ImmediateDecisions_ShareCompletedTasksAndSupportMultipleAwaiters(TaskSchedulerAdmissionResult result)
    {
        var first = Request();
        var second = Request();
        first.Decide(result);
        second.Decide(result);
        var decision = first.Decision;
        Assert.That(decision.IsCompletedSuccessfully, Is.True);
        Assert.That(first.Decision, Is.SameAs(decision));
        Assert.That(second.Decision, Is.SameAs(decision));
        Assert.That(await Task.WhenAll(decision, decision, second.Decision), Is.All.EqualTo(result));
    }

    [TestCase(TaskSchedulerAdmissionResult.Accepted)]
    [TestCase(TaskSchedulerAdmissionResult.Full)]
    [TestCase(TaskSchedulerAdmissionResult.WaiterLimitReached)]
    [TestCase(TaskSchedulerAdmissionResult.Closed)]
    public async Task PendingDecision_CompletesTheOriginalTaskAndCannotBeOverwritten(TaskSchedulerAdmissionResult result)
    {
        var request = Request();
        var decision = request.Decision;
        Assert.That(decision.IsCompleted, Is.False);
        Assert.That(request.Decision, Is.SameAs(decision));
        var both = Task.WhenAll(decision, decision);
        request.Decide(result);
        request.Cancel();
        request.Decide(TaskSchedulerAdmissionResult.Accepted);
        request.RecordCallerOutcome(new FormatException());
        request.OnQuiesced();
        Assert.That(request.Decision, Is.SameAs(decision));
        Assert.That(await both, Is.All.EqualTo(result));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Cancellation_RemainsCanceledBeforeOrAfterTaskMaterialization(bool materializeFirst)
    {
        var request = Request();
        var original = materializeFirst ? request.Decision : null;
        request.Cancel();
        var decision = request.Decision;
        if (original != null)
            Assert.That(decision, Is.SameAs(original));
        request.Decide(TaskSchedulerAdmissionResult.Accepted);
        request.RecordCallerOutcome(null);
        request.OnQuiesced();
        Assert.That(request.Decision, Is.SameAs(decision));
        Assert.That(decision.IsCanceled, Is.True);
        await Assert.ThatAsync(async () => await decision, Throws.InstanceOf<OperationCanceledException>());
        await Assert.ThatAsync(async () => await request.Decision, Throws.InstanceOf<OperationCanceledException>());
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UnreachedFailure_PreservesExceptionAndTaskIdentity(bool materializeFirst)
    {
        var request = Request();
        var original = materializeFirst ? request.Decision : null;
        var failure = new FormatException("Admission was never reached");
        request.RecordCallerOutcome(failure);
        request.OnQuiesced();
        var decision = request.Decision;
        if (original != null)
            Assert.That(decision, Is.SameAs(original));
        request.Decide(TaskSchedulerAdmissionResult.Accepted);
        request.Cancel();
        Assert.That(request.Decision, Is.SameAs(decision));
        await Assert.ThatAsync(async () => await decision, Throws.Exception.SameAs(failure));
        await Assert.ThatAsync(async () => await request.Decision, Throws.Exception.SameAs(failure));
    }

    [TestCase("accepted")]
    [TestCase("canceled")]
    [TestCase("faulted")]
    public async Task ConcurrentReadersAndResolution_ShareOneStableTask(string outcome)
    {
        for (var iteration = 0; iteration < 40; iteration++)
        {
            var request = Request();
            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var decisions = new Task<TaskSchedulerAdmissionResult>[8];
            var failure = new FormatException("Unreached admission");
            var readers = Enumerable.Range(0, decisions.Length).Select(index => Task.Run(async () =>
            {
                await start.Task;
                decisions[index] = request.Decision;
            })).ToArray();
            var resolve = Task.Run(async () =>
            {
                await start.Task;
                if (outcome == "accepted")
                    request.Decide(TaskSchedulerAdmissionResult.Accepted);
                else if (outcome == "canceled")
                    request.Cancel();
                else
                { request.RecordCallerOutcome(failure); request.OnQuiesced(); }
            });
            start.SetResult(true);
            await Task.WhenAll(readers.Append(resolve));
            Assert.That(decisions, Is.All.SameAs(request.Decision));
            if (outcome == "accepted")
                Assert.That(await request.Decision, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
            else if (outcome == "canceled")
                await Assert.ThatAsync(async () => await request.Decision, Throws.InstanceOf<OperationCanceledException>());
            else
                await Assert.ThatAsync(async () => await request.Decision, Throws.Exception.SameAs(failure));
        }
    }
}