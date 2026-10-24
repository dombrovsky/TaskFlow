namespace TaskFlow.Tests;

using NUnit.Framework;
using System.Threading.Tasks.Flow;

[TestFixture]
internal sealed class TaskSchedulerEnqueueExtensionsFixture
{
    private static IEnumerable<string> ExtensionOverloads => TaskSchedulerDisposalConformance.Overloads.Where(overload => overload != "DirectGeneric");

    [TestCase("Action")]
    [TestCase("ActionWithToken")]
    [TestCase("TokenAction")]
    [TestCase("TokenActionWithToken")]
    [TestCase("Task")]
    [TestCase("TokenTask")]
    [TestCase("TokenTaskWithToken")]
    [TestCase("StateTask")]
    [TestCase("ValueTask")]
    [TestCase("TokenValueTask")]
    [TestCase("TokenValueTaskWithToken")]
    [TestCase("StateValueTask")]
    public void Enqueue_ShouldReturnSchedulerTaskWithoutAdditionalContinuations(string overload)
    {
        var scheduler = new PendingScheduler();
        var task = Enqueue(scheduler, overload);

        // An extra async wrapper could still be pending after the scheduler has drained its queue.
        Assert.That(task, Is.SameAs(scheduler.ScheduledTask));
        Assert.That(task.IsCompleted, Is.False);
        scheduler.Complete!();
        Assert.That(task.IsCompletedSuccessfully, Is.True);
    }

    [TestCaseSource(nameof(ExtensionOverloads))]
    public void Enqueue_InvalidScheduler_ShouldThrowSynchronously(string overload)
    {
        Task? task = null;
        Assert.That(() => { task = Enqueue(null!, overload); }, Throws.TypeOf<ArgumentNullException>());
        Assert.That(task, Is.Null);
    }

    [TestCaseSource(typeof(TaskSchedulerDisposalConformance), nameof(TaskSchedulerDisposalConformance.Overloads))]
    public void Enqueue_SchedulingFailure_ShouldThrowSynchronously(string overload)
    {
        var failure = new ArgumentException("Scheduling rejected");
        var scheduler = new ThrowingScheduler(failure);
        Task? task = null;
        Assert.That(() => { task = Enqueue(scheduler, overload); }, Throws.Exception.SameAs(failure));
        Assert.That(task, Is.Null);
    }

    private static Task Enqueue(ITaskScheduler scheduler, string overload)
        => TaskSchedulerDisposalConformance.Enqueue(scheduler, overload, () => { }, false);

    private sealed class ThrowingScheduler(Exception failure) : ITaskScheduler
    {
        public Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
        {
            throw failure;
        }
    }

    private sealed class PendingScheduler : ITaskScheduler
    {
        public Task? ScheduledTask { get; private set; }

        public Action? Complete { get; private set; }

        public Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            ScheduledTask = completion.Task;
            Complete = () => completion.SetResult(default!);
            return completion.Task;
        }
    }
}