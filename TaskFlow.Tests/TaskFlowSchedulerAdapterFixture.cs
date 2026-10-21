namespace TaskFlow.Tests;

using NUnit.Framework;
using System.Threading.Tasks.Flow;

[TestFixture]
internal sealed class TaskFlowSchedulerAdapterFixture
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task Enqueue_QueueTaskFailure_ShouldPreserveStartNewException(bool disposed)
    {
        Exception failure = disposed ? new ObjectDisposedException("scheduler") : new InvalidOperationException("rejected");
        var scheduler = new TaskFlowSchedulerAdapter(new RejectingScheduler(failure));
        Task<int>? pending = null;

        Assert.That(() => { pending = scheduler.Enqueue((_, _) => new ValueTask<int>(42), null, CancellationToken.None); }, Throws.Nothing);
        await Assert.ThatAsync(async () => await pending!,
            Throws.TypeOf<TaskSchedulerException>().With.Property(nameof(Exception.InnerException)).SameAs(failure));
    }

    [Test]
    public async Task Enqueue_NullDelegate_ShouldRetainTaskBasedValidation()
    {
        var scheduler = new TaskFlowSchedulerAdapter(TaskScheduler.Default);
        Task<int>? pending = null;
        Assert.That(() => { pending = scheduler.Enqueue<int>(null!, null, CancellationToken.None); }, Throws.Nothing);
        await Assert.ThatAsync(async () => await pending!, Throws.TypeOf<ArgumentNullException>());
    }

    private sealed class RejectingScheduler(Exception failure) : TaskScheduler
    {
        protected override void QueueTask(Task task) => throw failure;
        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;
        protected override IEnumerable<Task> GetScheduledTasks() => [];
    }
}