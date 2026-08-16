namespace System.Threading.Tasks.Flow
{
    using Microsoft.Extensions.Logging;

    /// <summary>Configures lifecycle logging performed by <c>WithLogging</c>.</summary>
    public sealed class TaskFlowLoggingOptions
    {
        /// <summary>Gets or sets the level used when an operation is enqueued.</summary>
        public LogLevel EnqueuedLogLevel { get; set; } = LogLevel.Trace;

        /// <summary>Gets or sets the level used when an operation starts.</summary>
        public LogLevel StartedLogLevel { get; set; } = LogLevel.Trace;

        /// <summary>Gets or sets the level used when cancellation is requested.</summary>
        public LogLevel CancellationRequestedLogLevel { get; set; } = LogLevel.Trace;

        /// <summary>Gets or sets the level used when an operation succeeds.</summary>
        public LogLevel SucceededLogLevel { get; set; } = LogLevel.Trace;

        /// <summary>Gets or sets the level used when an operation fails or is canceled.</summary>
        public LogLevel FailedLogLevel { get; set; } = LogLevel.Trace;

        /// <summary>Gets or sets the level used when an operation finishes.</summary>
        public LogLevel FinishedLogLevel { get; set; } = LogLevel.Trace;

        /// <summary>Gets or sets an optional asynchronous interceptor invoked with the logging interceptor.</summary>
        public ITaskSchedulerInterceptor? Interceptor { get; set; }
    }
}
