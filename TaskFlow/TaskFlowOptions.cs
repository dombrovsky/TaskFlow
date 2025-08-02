namespace System.Threading.Tasks.Flow
{
    /// <summary>
    /// Represents configuration options for task flow instances.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TaskFlowOptions is an immutable record that provides configuration settings for
    /// controlling the behavior of task flow implementations.
    /// </para>
    /// <para>
    /// A default instance with sensible default values is available via the <see cref="Default"/> property.
    /// Custom instances can be created using object initializer syntax with the init-only properties.
    /// </para>
    /// <example>
    /// Creating custom options:
    /// <code>
    /// var options = new TaskFlowOptions
    /// {
    ///     TaskScheduler = TaskScheduler.FromCurrentSynchronizationContext(),
    ///     SynchronousDisposeTimeout = TimeSpan.FromSeconds(5)
    /// };
    /// 
    /// var taskFlow = new TaskFlow(options);
    /// </code>
    /// </example>
    /// </remarks>
    public sealed record TaskFlowOptions
    {
        /// <summary>
        /// Gets or sets the default options used when no specific options are provided.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The default instance has the following settings:
        /// </para>
        /// <list type="bullet">
        ///   <item><see cref="TaskScheduler"/> set to <see cref="TaskScheduler.Default"/></item>
        ///   <item><see cref="SynchronousDisposeTimeout"/> set to <see cref="Timeout.InfiniteTimeSpan"/></item>
        /// </list>
        /// <para>
        /// This property can be modified to change the default settings used throughout the application.
        /// </para>
        /// </remarks>
        public static TaskFlowOptions Default { get; set; } = new TaskFlowOptions();

        /// <summary>
        /// Gets the task scheduler that will be used to schedule task execution.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The task scheduler determines how and where tasks are executed.
        /// </para>
        /// <para>
        /// Common options include:
        /// </para>
        /// <list type="bullet">
        ///   <item><see cref="TaskScheduler.Default"/> - Uses the .NET thread pool (default)</item>
        ///   <item><see cref="TaskScheduler.FromCurrentSynchronizationContext"/> - Executes tasks on the specific context</item>
        /// </list>
        /// </remarks>
        public TaskScheduler TaskScheduler { get; init; } = TaskScheduler.Default;

        /// <summary>
        /// Gets maximum time to wait on synchronous <see cref="ITaskFlow.Dispose"/> call.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This timeout is only used for synchronous disposal operations through <see cref="IDisposable.Dispose"/>
        /// or <see cref="ITaskFlow.Dispose(TimeSpan)"/>.
        /// </para>
        /// <para>
        /// When a task flow is disposed synchronously, this value determines how long to wait for
        /// all pending tasks to complete before returning from the disposal operation.
        /// </para>
        /// <para>
        /// The default value is <see cref="Timeout.InfiniteTimeSpan"/>, which means the disposal
        /// will wait indefinitely for tasks to complete.
        /// </para>
        /// <para>
        /// This setting does not affect asynchronous disposal through <see cref="IAsyncDisposable.DisposeAsync"/>,

        /// which always waits for full completion.
        /// </para>
        /// </remarks>
        public TimeSpan SynchronousDisposeTimeout { get; init; } = Timeout.InfiniteTimeSpan;
    }
}