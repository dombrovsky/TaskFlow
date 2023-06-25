namespace System.Threading.Tasks.Flow
{
    public sealed record TaskFlowOptions
    {
        public static TaskFlowOptions Default { get; set; } = new TaskFlowOptions();

        public TaskScheduler TaskScheduler { get; init; } = TaskScheduler.Default;

        /// <summary>
        /// Gets maximum time to wait on synchronous <see cref="ITaskFlow.Dispose"/> call.
        /// Default value is <see cref="Timeout.InfiniteTimeSpan"/>.
        /// </summary>
        /// <remarks>
        /// Does not affect asynchronous <see cref="ITaskFlow.DisposeAsync"/> call.
        /// </remarks>
        public TimeSpan SynchronousDisposeTimeout { get; init; } = Timeout.InfiniteTimeSpan;
    }
}