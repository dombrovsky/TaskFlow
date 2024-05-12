namespace System.Threading.Tasks.Flow
{
    using System.Threading.Tasks.Flow.Annotations;
    using System.Threading.Tasks.Flow.Internal;

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
            Argument.NotNull(d);

            _taskScheduler.Enqueue(() => d(state)).Await();
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            Argument.NotNull(d);

            _ = _taskScheduler.Enqueue(() => d(state));
        }
    }
}