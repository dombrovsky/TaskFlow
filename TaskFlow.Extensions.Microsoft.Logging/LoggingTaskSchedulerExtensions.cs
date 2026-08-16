namespace System.Threading.Tasks.Flow
{
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

        /// <summary>Wraps a scheduler with configurable structured lifecycle logging.</summary>
        /// <remarks>
        /// Place <c>WithOperationName</c> outside this decorator, for example
        /// <c>scheduler.WithLogging(logger).WithOperationName("Import")</c>, so logging can observe the annotation.
        /// </remarks>
        public static ITaskScheduler WithLogging(this ITaskScheduler taskScheduler, ILogger logger, Action<TaskFlowLoggingOptions>? configure = null)
        {
            Argument.NotNull(taskScheduler);
            Argument.NotNull(logger);

            var options = new TaskFlowLoggingOptions();
            configure?.Invoke(options);
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
                var operation = new LoggingOperationState<T>(
                    Interlocked.Increment(ref _lastOperationId),
                    TaskSchedulerInterceptionContext.GetAnnotation<OperationNameAnnotation>(state)?.OperationName,
                    taskFunc,
                    state);

                if (_logger.IsEnabled(_options.EnqueuedLogLevel))
                {
                    _logger.Log(_options.EnqueuedLogLevel, EnqueuedEvent,
                        "TaskFlow operation {OperationId} ({OperationName}) enqueued", operation.OperationId, operation.OperationName);
                }

                using var registration = cancellationToken.Register(() =>
                {
                    if (_logger.IsEnabled(_options.CancellationRequestedLogLevel))
                    {
                        _logger.Log(_options.CancellationRequestedLogLevel, CancellationRequestedEvent,
                            "Cancellation requested for TaskFlow operation {OperationId} ({OperationName})", operation.OperationId, operation.OperationName);
                    }
                });

                return await _interceptedScheduler.Enqueue(Execute, operation, cancellationToken).ConfigureAwait(false);

                static ValueTask<T> Execute(object? operationState, CancellationToken token)
                {
                    var loggingState = (LoggingOperationState<T>)operationState!;
                    return loggingState.TaskFunc(loggingState.State, token);
                }
            }
        }

        private struct LoggingInterceptor : ITaskSchedulerInterceptor
        {
            private readonly ILogger _logger;
            private readonly TaskFlowLoggingOptions _options;
            private long _startTimestamp;

            public LoggingInterceptor(ILogger logger, TaskFlowLoggingOptions options)
            {
                _logger = logger;
                _options = options;
                _startTimestamp = 0;
            }

            public void OnBefore(TaskSchedulerInterceptionContext context)
            {
                if (_logger.IsEnabled(_options.SucceededLogLevel) || _logger.IsEnabled(_options.FailedLogLevel) || _logger.IsEnabled(_options.FinishedLogLevel))
                {
                    _startTimestamp = Stopwatch.GetTimestamp();
                }

                if (_logger.IsEnabled(_options.StartedLogLevel))
                {
                    var operation = GetLoggingState(context);
                    _logger.Log(_options.StartedLogLevel, StartedEvent,
                        "TaskFlow operation {OperationId} ({OperationName}) started", operation.OperationId, operation.OperationName);
                }

            }

            public void OnSuccess<TResult>(TaskSchedulerInterceptionContext context, TResult result)
            {
                if (_logger.IsEnabled(_options.SucceededLogLevel))
                {
                    var operation = GetLoggingState(context);
                    _logger.Log(_options.SucceededLogLevel, SucceededEvent,
                        "TaskFlow operation {OperationId} ({OperationName}) succeeded with result type {ResultType} in {ElapsedMilliseconds} ms",
                        operation.OperationId, operation.OperationName, typeof(TResult).FullName, GetElapsedMilliseconds());
                }

            }

            public void OnError(TaskSchedulerInterceptionContext context, Exception exception)
            {
                if (_logger.IsEnabled(_options.FailedLogLevel))
                {
                    var operation = GetLoggingState(context);
                    _logger.Log(_options.FailedLogLevel, FailedEvent, exception,
                        "TaskFlow operation {OperationId} ({OperationName}) failed in {ElapsedMilliseconds} ms",
                        operation.OperationId, operation.OperationName, GetElapsedMilliseconds());
                }

            }

            public void OnFinally(TaskSchedulerInterceptionContext context)
            {
                LogFinished(context);
            }

            private void LogFinished(TaskSchedulerInterceptionContext context)
            {
                if (_logger.IsEnabled(_options.FinishedLogLevel))
                {
                    var operation = GetLoggingState(context);
                    _logger.Log(_options.FinishedLogLevel, FinishedEvent,
                        "TaskFlow operation {OperationId} ({OperationName}) finished in {ElapsedMilliseconds} ms",
                        operation.OperationId, operation.OperationName, GetElapsedMilliseconds());
                }
            }

            private double GetElapsedMilliseconds()
            {
                return _startTimestamp != 0
                    ? (Stopwatch.GetTimestamp() - _startTimestamp) * 1000d / Stopwatch.Frequency
                    : 0d;
            }

            private static ILoggingOperationState GetLoggingState(TaskSchedulerInterceptionContext context)
            {
                return (ILoggingOperationState)context.State!;
            }
        }

        private interface ILoggingOperationState
        {
            long OperationId { get; }
            string? OperationName { get; }
        }

        private sealed class LoggingOperationState<T> : ILoggingOperationState
        {
            public LoggingOperationState(long operationId, string? operationName, Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state)
            {
                OperationId = operationId;
                OperationName = operationName;
                TaskFunc = taskFunc;
                State = state;
            }

            public long OperationId { get; }
            public string? OperationName { get; }
            public Func<object?, CancellationToken, ValueTask<T>> TaskFunc { get; }
            public object? State { get; }
        }
    }
}
