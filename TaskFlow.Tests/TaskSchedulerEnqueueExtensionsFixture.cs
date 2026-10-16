namespace TaskFlow.Tests;

using NUnit.Framework;
using System.Threading.Tasks.Flow;

[TestFixture]
internal sealed class TaskSchedulerEnqueueExtensionsFixture
{
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
        using var cancellation = new CancellationTokenSource();
        var task = overload switch
        {
            "Action" => scheduler.Enqueue(() => { }),
            "ActionWithToken" => scheduler.Enqueue(() => { }, cancellation.Token),
            "TokenAction" => scheduler.Enqueue(token => { }),
            "TokenActionWithToken" => scheduler.Enqueue(token => { }, cancellation.Token),
            "Task" => scheduler.Enqueue(() => Task.CompletedTask),
            "TokenTask" => scheduler.Enqueue(token => Task.CompletedTask),
            "TokenTaskWithToken" => scheduler.Enqueue(token => Task.CompletedTask, cancellation.Token),
            "StateTask" => scheduler.Enqueue((int state, CancellationToken token) => Task.CompletedTask, 42, cancellation.Token),
            "ValueTask" => scheduler.Enqueue(() => ValueTask.CompletedTask),
            "TokenValueTask" => scheduler.Enqueue(token => ValueTask.CompletedTask),
            "TokenValueTaskWithToken" => scheduler.Enqueue(token => ValueTask.CompletedTask, cancellation.Token, null),
            "StateValueTask" => scheduler.Enqueue((int state, CancellationToken token) => ValueTask.CompletedTask, 42, cancellation.Token),
            _ => throw new ArgumentOutOfRangeException(nameof(overload)),
        };

        // An extra async wrapper could still be pending after the scheduler has drained its queue.
        Assert.That(task, Is.SameAs(scheduler.ScheduledTask));
        Assert.That(task.IsCompleted, Is.False);
        scheduler.Complete!();
        Assert.That(task.IsCompletedSuccessfully, Is.True);
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