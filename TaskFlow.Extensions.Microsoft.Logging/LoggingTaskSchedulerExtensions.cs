namespace System.Threading.Tasks.Flow
{
    using System.Diagnostics;
    using Microsoft.Extensions.Logging;
    using System.Threading.Tasks.Flow.Annotations;

    /// <summary>Adds Microsoft.Extensions.Logging structured lifecycle events to task schedulers.</summary>
    public static class LoggingTaskSchedulerExtensions
    {
        private static readonly EventId EnqueuedEvent = new EventId(0x5446_0001, "TaskFlowOperationEnqueued");
        private static readonly EventId StartedEvent = new EventId(0x5446_0002, "TaskFlowOperationStarted");
        private static readonly EventId CancellationRequestedEvent = new EventId(0x5446_0003, "TaskFlowOperationCancellationRequested");
        private static readonly EventId SucceededEvent = new EventId(0x5446_0004, "TaskFlowOperationSucceeded");
        private static readonly EventId FailedEvent = new EventId(0x5446_0005, "TaskFlowOperationFailed");
        private static readonly EventId FinishedEvent = new EventId(0x5446_0006, "TaskFlowOperationFinished");

        /// <summary>Registers structured enqueue and execution lifecycle logging for every scheduled operation.</summary>
        /// <param name="taskScheduler">The scheduler whose operations will be logged.</param>
        /// <param name="logger">The logger that receives lifecycle events.</param>
        /// <param name="configure">
        /// An optional callback that configures event levels. When omitted, every lifecycle event uses
        /// <see cref="LogLevel.Trace"/>.
        /// </param>
        /// <returns>A new immutable scheduler snapshot that logs operation lifecycles.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="taskScheduler"/> or <paramref name="logger"/> is <c>null</c>.
        /// </exception>
        /// <remarks>
        /// <para>
        /// The compound middleware emits enqueue, start, cancellation-request, success or failure, and finish events.
        /// Every event carries an increasing operation ID and the <see cref="OperationNameAnnotation"/> visible when
        /// this logging registration is created. Result type and elapsed duration are included where applicable.
        /// </para>
        /// <para>
        /// Call <see cref="AnnotatingTaskSchedulerExtensions.WithOperationName(ITaskScheduler, string)"/> before
        /// <c>WithLogging</c>. Metadata is forward-scoped, so an operation name registered later does not retroactively
        /// change this logging registration.
        /// </para>
        /// <para>
        /// <see cref="ILogger.IsEnabled(LogLevel)"/> is checked before each event and before optional timing work begins.
        /// Disabled events do not invoke <see cref="ILogger.Log{TState}(LogLevel, EventId, TState, Exception, Func{TState, Exception, string})"/>.
        /// Cancellation-request logging observes the submitting caller's token and does not imply that the operation
        /// ultimately completes as canceled. Logging never suppresses or replaces the operation outcome.
        /// </para>
        /// <para>The returned pipeline snapshot is non-owning and does not dispose the underlying scheduler or logger.</para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var scheduler = taskFlow
        ///     .WithOperationName("imports.run")
        ///     .WithLogging(logger, options =&gt;
        ///     {
        ///         options.StartedLogLevel = LogLevel.Information;
        ///         options.FailedLogLevel = LogLevel.Error;
        ///         options.FinishedLogLevel = LogLevel.Debug;
        ///     });
        ///
        /// await scheduler.Enqueue(token =&gt; ImportAsync(token));
        /// </code>
        /// </example>
        public static ITaskScheduler WithLogging(this ITaskScheduler taskScheduler, ILogger logger, Action<TaskFlowLoggingOptions>? configure = null)
        {
            Argument.NotNull(taskScheduler);
            Argument.NotNull(logger);
            var options = new TaskFlowLoggingOptions();
            configure?.Invoke(options);
            return taskScheduler.UseMiddleware(new LoggingMiddleware(logger, options));
        }

        private sealed class LoggingMiddleware : ITaskSchedulerEnqueueMiddleware, ITaskSchedulerExecutionMiddleware
        {
            private readonly ILogger _logger;
            private readonly TaskFlowLoggingOptions _options;
            private long _lastOperationId;

            public LoggingMiddleware(ILogger logger, TaskFlowLoggingOptions options)
            {
                _logger = logger;
                _options = options;
            }

            public async Task<TResult> InvokeAsync<TResult>(TaskSchedulerEnqueueContext<TResult> context, TaskSchedulerEnqueueDelegate<TResult> continuation)
            {
                var operation = context.GetOrCreateLocalState(() => new LoggingOperationState(
                    Interlocked.Increment(ref _lastOperationId),
                    context.GetAnnotation<OperationNameAnnotation>()?.OperationName));

                if (_logger.IsEnabled(_options.EnqueuedLogLevel))
                {
                    _logger.Log(_options.EnqueuedLogLevel, EnqueuedEvent,
                        "TaskFlow operation {OperationId} ({OperationName}) enqueued", operation.OperationId, operation.OperationName);
                }

                using var registration = context.CallerCancellationToken.Register(() =>
                {
                    if (_logger.IsEnabled(_options.CancellationRequestedLogLevel))
                    {
                        _logger.Log(_options.CancellationRequestedLogLevel, CancellationRequestedEvent,
                            "Cancellation requested for TaskFlow operation {OperationId} ({OperationName})", operation.OperationId, operation.OperationName);
                    }
                });

                return await continuation(context).ConfigureAwait(false);
            }

            public async ValueTask<TResult> InvokeAsync<TResult>(TaskSchedulerOperationContext context, TaskSchedulerExecutionDelegate<TResult> continuation)
            {
                var operation = context.GetOrCreateLocalState(() => new LoggingOperationState(
                    Interlocked.Increment(ref _lastOperationId),
                    context.GetAnnotation<OperationNameAnnotation>()?.OperationName));

                if (_logger.IsEnabled(_options.SucceededLogLevel) || _logger.IsEnabled(_options.FailedLogLevel) || _logger.IsEnabled(_options.FinishedLogLevel))
                {
                    operation.StartTimestamp = Stopwatch.GetTimestamp();
                }

                if (_logger.IsEnabled(_options.StartedLogLevel))
                {
                    _logger.Log(_options.StartedLogLevel, StartedEvent,
                        "TaskFlow operation {OperationId} ({OperationName}) started", operation.OperationId, operation.OperationName);
                }

                try
                {
                    TResult result;
                    try
                    {
                        result = await continuation(context).ConfigureAwait(true);
                    }
                    catch (Exception exception)
                    {
                        if (_logger.IsEnabled(_options.FailedLogLevel))
                        {
                            _logger.Log(_options.FailedLogLevel, FailedEvent, exception,
                                "TaskFlow operation {OperationId} ({OperationName}) failed in {ElapsedMilliseconds} ms",
                                operation.OperationId, operation.OperationName, operation.GetElapsedMilliseconds());
                        }

                        throw;
                    }

                    if (_logger.IsEnabled(_options.SucceededLogLevel))
                    {
                        _logger.Log(_options.SucceededLogLevel, SucceededEvent,
                            "TaskFlow operation {OperationId} ({OperationName}) succeeded with result type {ResultType} in {ElapsedMilliseconds} ms",
                            operation.OperationId, operation.OperationName, typeof(TResult).FullName, operation.GetElapsedMilliseconds());
                    }

                    return result;
                }
                finally
                {
                    if (_logger.IsEnabled(_options.FinishedLogLevel))
                    {
                        _logger.Log(_options.FinishedLogLevel, FinishedEvent,
                            "TaskFlow operation {OperationId} ({OperationName}) finished in {ElapsedMilliseconds} ms",
                            operation.OperationId, operation.OperationName, operation.GetElapsedMilliseconds());
                    }
                }
            }
        }

        private sealed class LoggingOperationState
        {
            public LoggingOperationState(long operationId, string? operationName)
            {
                OperationId = operationId;
                OperationName = operationName;
            }

            public long OperationId { get; }
            public string? OperationName { get; }
            public long StartTimestamp { get; set; }

            public double GetElapsedMilliseconds() => StartTimestamp == 0
                ? 0d
                : (Stopwatch.GetTimestamp() - StartTimestamp) * 1000d / Stopwatch.Frequency;
        }
    }
}
