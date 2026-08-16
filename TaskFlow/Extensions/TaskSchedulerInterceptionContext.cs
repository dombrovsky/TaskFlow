namespace System.Threading.Tasks.Flow
{
    /// <summary>Describes an operation observed by an <see cref="ITaskSchedulerInterceptor"/>.</summary>
    public sealed class TaskSchedulerInterceptionContext
    {
        internal TaskSchedulerInterceptionContext(long operationId, string? operationName, object? state, CancellationToken cancellationToken)
        {
            OperationId = operationId;
            OperationName = operationName;
            State = state;
            CancellationToken = cancellationToken;
        }

        /// <summary>Gets the identifier assigned by the interception wrapper.</summary>
        public long OperationId { get; }

        /// <summary>Gets the annotated operation name, when one is available.</summary>
        public string? OperationName { get; }

        /// <summary>Gets the original state supplied by the caller.</summary>
        public object? State { get; }

        /// <summary>Gets the cancellation token supplied to the scheduler.</summary>
        public CancellationToken CancellationToken { get; }
    }
}
