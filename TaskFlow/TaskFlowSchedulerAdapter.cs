namespace System.Threading.Tasks.Flow
{
    using System.Threading.Tasks.Flow.Annotations;

    public sealed class TaskFlowSchedulerAdapter : ITaskScheduler
    {
        private readonly TaskScheduler _taskScheduler;

        public TaskFlowSchedulerAdapter(TaskScheduler taskScheduler)
        {
            Argument.NotNull(taskScheduler);

            _taskScheduler = taskScheduler;
        }

        public async ValueTask<T> Enqueue<T>(Func<CancellationToken, ValueTask<T>> taskFunc, CancellationToken cancellationToken)
        {
            Argument.NotNull(taskFunc);

            var task = await Task.Factory.StartNew(() => taskFunc(cancellationToken), cancellationToken, TaskCreationOptions.None, _taskScheduler).ConfigureAwait(false);
            return await task.ConfigureAwait(false);
        }
    }
}