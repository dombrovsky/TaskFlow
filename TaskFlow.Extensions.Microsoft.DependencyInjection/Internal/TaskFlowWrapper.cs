namespace System.Threading.Tasks.Flow.Internal
{
    using System.Threading.Tasks.Flow.Annotations;

    internal sealed class TaskFlowWrapper : ITaskFlow
    {
        private readonly ITaskFlow _taskFlow;

        public TaskFlowWrapper(ITaskFlow taskFlow)
        {
            Argument.NotNull(taskFlow);

            _taskFlow = taskFlow;
        }

        public TaskFlowOptions Options => _taskFlow.Options;

        public ValueTask DisposeAsync()
        {
            return _taskFlow.DisposeAsync();
        }

        public void Dispose()
        {
            _taskFlow.Dispose();
        }

        public bool Dispose(TimeSpan timeout)
        {
            return _taskFlow.Dispose(timeout);
        }

        public Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
        {
            return _taskFlow.Enqueue(taskFunc, state, cancellationToken);
        }
    }
}