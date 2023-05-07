namespace System.Threading.Tasks.Flow
{
    using System.Threading.Tasks.Flow.Annotations;

    public sealed class TaskFlowSynchronizationContext : SynchronizationContext
    {
        private readonly ITaskScheduler _taskScheduler;

        public TaskFlowSynchronizationContext(ITaskScheduler taskScheduler)
        {
            Argument.NotNull(taskScheduler);

            _taskScheduler = taskScheduler;
        }

        public static SynchronizationContext For(ITaskScheduler taskScheduler)
        {
            return new TaskFlowSynchronizationContext(taskScheduler);
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            _taskScheduler.Enqueue(() => d(state)).AsTask().Await();
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            _taskScheduler.Enqueue(() => d(state)).AsTask();
        }
    }
}