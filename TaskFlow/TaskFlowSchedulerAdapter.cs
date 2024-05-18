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

        public async Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
        {
            Argument.NotNull(taskFunc);

            var task = await Task.Factory.StartNew(() => taskFunc(state, cancellationToken), cancellationToken, TaskCreationOptions.None, _taskScheduler).ConfigureAwait(false);
            return await task.ConfigureAwait(false);
        }
    }
}