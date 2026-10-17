namespace TaskFlow.Tests;

using NUnit.Framework;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks.Flow;

[TestFixture]
internal sealed class TaskSchedulerEnqueueExtensionsFixture
{
    private static readonly string[] OriginallyAsyncOverloads =
    [
        "Action", "ActionWithToken", "TokenAction", "TokenActionWithToken",
        "TokenTask", "TokenTaskWithToken", "TokenValueTask", "TokenValueTaskWithToken",
    ];
    private static readonly string[] AsyncFailureOverloads =
    [
        .. OriginallyAsyncOverloads, "Task", "ValueTask",
    ];

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
        var task = Enqueue(scheduler, overload, cancellation.Token);

        // An extra async wrapper could still be pending after the scheduler has drained its queue.
        Assert.That(task, Is.SameAs(scheduler.ScheduledTask));
        Assert.That(task.IsCompleted, Is.False);
        scheduler.Complete!();
        Assert.That(task.IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public async Task Enqueue_InvalidScheduler_ShouldReturnFaultedTask([ValueSource(nameof(OriginallyAsyncOverloads))] string overload)
    {
        Task? task = null;
        Assert.That(() => { task = Enqueue(null!, overload, CancellationToken.None); }, Throws.Nothing);
        Assert.That(task!.IsFaulted, Is.True);
        await Assert.ThatAsync(async () => await task, Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public async Task Enqueue_SynchronousSchedulingFailure_ShouldSettleReturnedTask(
        [ValueSource(nameof(AsyncFailureOverloads))] string overload,
        [Values("failure", "cancellation", "uncanceledCancellation")] string outcome)
    {
        using var cancellation = new CancellationTokenSource();
        if (outcome == "cancellation")
            await cancellation.CancelAsync();
        Exception failure = outcome == "failure"
            ? new InvalidOperationException("Scheduling failed")
            : new OperationCanceledException("Scheduling canceled", cancellation.Token);
        var scheduler = new ThrowingScheduler(failure);
        Task? task = null;
        Assert.That(() => { task = Enqueue(scheduler, overload, cancellation.Token); }, Throws.Nothing);
        Assert.That(task!.IsCanceled, Is.EqualTo(outcome != "failure"));
        Assert.That(task.IsFaulted, Is.EqualTo(outcome == "failure"));
        await Assert.ThatAsync(async () => await task, Throws.Exception.SameAs(failure));
    }

    [SuppressMessage("Reliability", "CA2016:Forward the CancellationToken parameter to methods that take one", Justification = "The cases intentionally exercise overloads without a cancellation token.")]
    private static Task Enqueue(ITaskScheduler scheduler, string overload, CancellationToken cancellationToken)
    {
        return overload switch
        {
            "Action" => scheduler.Enqueue(() => { }),
            "ActionWithToken" => scheduler.Enqueue(() => { }, cancellationToken),
            "TokenAction" => scheduler.Enqueue(token => { }),
            "TokenActionWithToken" => scheduler.Enqueue(token => { }, cancellationToken),
            "Task" => scheduler.Enqueue(() => Task.CompletedTask),
            "TokenTask" => scheduler.Enqueue(token => Task.CompletedTask),
            "TokenTaskWithToken" => scheduler.Enqueue(token => Task.CompletedTask, cancellationToken),
            "StateTask" => scheduler.Enqueue((int state, CancellationToken token) => Task.CompletedTask, 42, cancellationToken),
            "ValueTask" => scheduler.Enqueue(() => ValueTask.CompletedTask, null),
            "TokenValueTask" => scheduler.Enqueue(token => ValueTask.CompletedTask, null),
            "TokenValueTaskWithToken" => scheduler.Enqueue(token => ValueTask.CompletedTask, cancellationToken, null),
            "StateValueTask" => scheduler.Enqueue((int state, CancellationToken token) => ValueTask.CompletedTask, 42, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(overload)),
        };
    }

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