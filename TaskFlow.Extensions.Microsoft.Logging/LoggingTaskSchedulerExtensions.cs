namespace System.Threading.Tasks.Flow
{
    using System.Collections.Concurrent;
    using System.Diagnostics;
    using Microsoft.Extensions.Logging;
    using System.Threading.Tasks.Flow.Annotations;

    /// <summary>Adds structured lifecycle logging to task schedulers.</summary>
    public static class LoggingTaskSchedulerExtensions
    {
        private static readonly EventId EnqueuedEvent = new EventId(0x5446_0001, "TaskFlowOperationEnqueued");
        private static readonly EventId StartedEvent = new EventId(0x5446_0002, "TaskFlowOperationStarted");
        private static readonly EventId CancellationRequestedEvent = new EventId(0x5446_0003, "TaskFlowOperationCancellationRequested");
        private static readonly EventId SucceededEvent = new EventId(0x5446_0004, "TaskFlowOperationSucceeded");
        private static readonly EventId FailedEvent = new EventId(0x5446_0005, "TaskFlowOperationFailed");
        private static readonly EventId FinishedEvent = new EventId(0x5446_0006, "TaskFlowOperationFinished");

        /// <summary>Wraps a scheduler with trace-level structured lifecycle logging.</summary>
        public static ITaskScheduler WithLogging(this ITaskScheduler taskScheduler, ILogger logger)
        {
            return WithLogging(taskScheduler, logger, _ => { });
        }

        /// <summary>Wraps a scheduler with configurable structured lifecycle logging.</summary>
        /// <remarks>
        /// Place <c>WithOperationName</c> outside this decorator, for example
        /// <c>scheduler.WithLogging(logger).WithOperationName("Import")</c>, so logging can observe the annotation.
        /// </remarks>
        public static ITaskScheduler WithLogging(this ITaskScheduler taskScheduler, ILogger logger, Action<TaskFlowLoggingOptions> configure)
        {
            Argument.NotNull(taskScheduler);
            Argument.NotNull(logger);
            Argument.NotNull(configure);

            var options = new TaskFlowLoggingOptions();
            configure(options);
            return new LoggingTaskSchedulerWrapper(taskScheduler, logger, options);
        }

        private sealed class LoggingTaskSchedulerWrapper : ITaskScheduler
        {
            private readonly ILogger _logger;
            private readonly TaskFlowLoggingOptions _options;
            private readonly ITaskScheduler _interceptedScheduler;
            private long _lastOperationId;

            public LoggingTaskSchedulerWrapper(ITaskScheduler taskScheduler, ILogger logger, TaskFlowLoggingOptions options)
            {
                _logger = logger;
                _options = options;
                _interceptedScheduler = taskScheduler.Intercept(new LoggingInterceptor(logger, options));
            }

            public async Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
            {
                var operationId = Interlocked.Increment(ref _lastOperationId);
                string? operationName = null;

                if (_logger.IsEnabled(_options.EnqueuedLogLevel))
                {
                    _logger.Log(_options.EnqueuedLogLevel, EnqueuedEvent,
                        "TaskFlow operation {OperationId} ({OperationName}) enqueued", operationId, operationName);
                }

                using var registration = cancellationToken.Register(() =>
                {
                    if (_logger.IsEnabled(_options.CancellationRequestedLogLevel))
                    {
                        _logger.Log(_options.CancellationRequestedLogLevel, CancellationRequestedEvent,
                            "Cancellation requested for TaskFlow operation {OperationId} ({OperationName})", operationId, operationName);
                    }
                });

                return await _interceptedScheduler.Enqueue(taskFunc, state, cancellationToken).ConfigureAwait(false);
            }
        }

        private sealed class LoggingInterceptor : ITaskSchedulerInterceptor
        {
            private readonly ILogger _logger;
            private readonly TaskFlowLoggingOptions _options;
            private readonly ConcurrentDictionary<long, long> _startTimestamps = new ConcurrentDictionary<long, long>();

            public LoggingInterceptor(ILogger logger, TaskFlowLoggingOptions options)
            {
                _logger = logger;
                _options = options;
            }

            public ValueTask OnBeforeAsync(TaskSchedulerInterceptionContext context)
            {
                if (_logger.IsEnabled(_options.SucceededLogLevel) || _logger.IsEnabled(_options.FailedLogLevel) || _logger.IsEnabled(_options.FinishedLogLevel))
                {
                    _startTimestamps[context.OperationId] = Stopwatch.GetTimestamp();
                }

                if (_logger.IsEnabled(_options.StartedLogLevel))
                {
                    _logger.Log(_options.StartedLogLevel, StartedEvent,
                        "TaskFlow operation {OperationId} ({OperationName}) started", context.OperationId, context.OperationName);
                }

                return default;
            }

            public ValueTask OnSuccessAsync<TResult>(TaskSchedulerInterceptionContext context, TResult result)
            {
                if (_logger.IsEnabled(_options.SucceededLogLevel))
                {
                    _logger.Log(_options.SucceededLogLevel, SucceededEvent,
                        "TaskFlow operation {OperationId} ({OperationName}) succeeded with result type {ResultType} in {ElapsedMilliseconds} ms",
                        context.OperationId, context.OperationName, typeof(TResult).FullName, GetElapsedMilliseconds(context.OperationId));
                }

                return default;
            }

            public ValueTask OnErrorAsync(TaskSchedulerInterceptionContext context, Exception exception)
            {
                if (_logger.IsEnabled(_options.FailedLogLevel))
                {
                    _logger.Log(_options.FailedLogLevel, FailedEvent, exception,
                        "TaskFlow operation {OperationId} ({OperationName}) failed in {ElapsedMilliseconds} ms",
                        context.OperationId, context.OperationName, GetElapsedMilliseconds(context.OperationId));
                }

                return default;
            }

            public ValueTask OnFinallyAsync(TaskSchedulerInterceptionContext context)
            {
                LogFinished(context);
                return default;
            }

            private void LogFinished(TaskSchedulerInterceptionContext context)
            {
                if (_logger.IsEnabled(_options.FinishedLogLevel))
                {
                    _logger.Log(_options.FinishedLogLevel, FinishedEvent,
                        "TaskFlow operation {OperationId} ({OperationName}) finished in {ElapsedMilliseconds} ms",
                        context.OperationId, context.OperationName, GetElapsedMilliseconds(context.OperationId));
                }

                _startTimestamps.TryRemove(context.OperationId, out _);
            }

            private double GetElapsedMilliseconds(long operationId)
            {
                return _startTimestamps.TryGetValue(operationId, out var started)
                    ? (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency
                    : 0d;
            }
        }
    }
}
