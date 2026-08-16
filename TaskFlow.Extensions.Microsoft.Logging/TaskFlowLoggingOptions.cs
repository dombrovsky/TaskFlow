namespace System.Threading.Tasks.Flow
{
    using Microsoft.Extensions.Logging;

    /// <summary>Configures the lifecycle event levels used by <see cref="LoggingTaskSchedulerExtensions.WithLogging"/>.</summary>
    /// <remarks>
    /// Every event defaults to <see cref="LogLevel.Trace"/> and may be disabled with <see cref="LogLevel.None"/>.
    /// Each level is checked with <see cref="ILogger.IsEnabled(LogLevel)"/> before its event data is produced.
    /// </remarks>
    public sealed class TaskFlowLoggingOptions
    {
        /// <summary>Gets or sets the level used after an operation is accepted by the logging decorator.</summary>
        /// <value>The enqueue event level. The default is <see cref="LogLevel.Trace"/>.</value>
        public LogLevel EnqueuedLogLevel { get; set; } = LogLevel.Trace;

        /// <summary>Gets or sets the level used immediately before an operation starts executing.</summary>
        /// <value>The start event level. The default is <see cref="LogLevel.Trace"/>.</value>
        public LogLevel StartedLogLevel { get; set; } = LogLevel.Trace;

        /// <summary>Gets or sets the level used when cancellation is requested through the enqueue token.</summary>
        /// <value>The cancellation-request event level. The default is <see cref="LogLevel.Trace"/>.</value>
        /// <remarks>This event reports a request; the operation may still complete successfully.</remarks>
        public LogLevel CancellationRequestedLogLevel { get; set; } = LogLevel.Trace;

        /// <summary>Gets or sets the level used after an operation returns successfully.</summary>
        /// <value>The success event level. The default is <see cref="LogLevel.Trace"/>.</value>
        public LogLevel SucceededLogLevel { get; set; } = LogLevel.Trace;

        /// <summary>Gets or sets the level used when an operation fails or observes cancellation.</summary>
        /// <value>The failure event level. The default is <see cref="LogLevel.Trace"/>.</value>
        /// <remarks>The operation exception is attached to this log event.</remarks>
        public LogLevel FailedLogLevel { get; set; } = LogLevel.Trace;

        /// <summary>Gets or sets the level used after the operation lifecycle finishes, regardless of outcome.</summary>
        /// <value>The finish event level. The default is <see cref="LogLevel.Trace"/>.</value>
        public LogLevel FinishedLogLevel { get; set; } = LogLevel.Trace;
    }
}
