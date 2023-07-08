namespace System.Threading.Tasks.Flow.Internal
{
    using System.Threading.Tasks.Flow.Annotations;

    internal sealed class TaskFlowOwnershipWrapper : ITaskFlow
    {
        private readonly ITaskFlow _rootTaskFlow;
        private readonly ITaskScheduler _taskScheduler;

        public TaskFlowOwnershipWrapper(ITaskFlow rootTaskFlow, ITaskScheduler taskScheduler)
        {
            Argument.NotNull(rootTaskFlow);
            Argument.NotNull(taskScheduler);

            _rootTaskFlow = rootTaskFlow;
            _taskScheduler = taskScheduler;
        }

        public TaskFlowOptions Options => _rootTaskFlow.Options;

        public ValueTask DisposeAsync()
        {
            return _rootTaskFlow.DisposeAsync();
        }

        public void Dispose()
        {
            _rootTaskFlow.Dispose();
        }

        public bool Dispose(TimeSpan timeout)
        {
            return _rootTaskFlow.Dispose(timeout);
        }

        public ValueTask<T> Enqueue<T>(Func<CancellationToken, ValueTask<T>> taskFunc, CancellationToken cancellationToken)
        {
            return _taskScheduler.Enqueue(taskFunc, cancellationToken);
        }
    }
}