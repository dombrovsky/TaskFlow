namespace System.Threading.Tasks.Flow
{
    using System.Linq;
    using System.Threading.Tasks.Flow.Annotations;

    /// <summary>
    /// Provides extension methods for <see cref="ITaskScheduler"/> to add error handling capabilities with optional operation annotation support.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class enables sophisticated error handling patterns for task scheduler operations, including
    /// exception filtering, type-specific handling, and integration with operation annotations for
    /// contextual error processing. The error handlers are invoked when exceptions occur during
    /// task execution, but they do not suppress the exceptions - they provide a way to observe and
    /// respond to errors while maintaining normal exception propagation.
    /// </para>
    /// <para>
    /// Key features of the error handling system:
    /// </para>
    /// <list type="bullet">
    ///   <item>Type-specific exception handling with generic constraints</item>
    ///   <item>Optional exception filtering to handle only specific error conditions</item>
    ///   <item>Integration with operation annotations for contextual error information</item>
    ///   <item>Non-suppressing behavior - exceptions continue to propagate after handling</item>
    ///   <item>Access to the scheduler instance for reactive error handling</item>
    /// </list>
    /// <para>
    /// Error handlers can be used for:
    /// </para>
    /// <list type="bullet">
    ///   <item>Logging errors with operation context</item>
    ///   <item>Sending error notifications or alerts</item>
    ///   <item>Recording metrics and telemetry</item>
    ///   <item>Triggering compensating actions</item>
    ///   <item>Enriching error information with operation metadata</item>
    /// </list>
    /// <para>
    /// The error handling system works by wrapping the base scheduler and intercepting exceptions
    /// that occur during task execution. Multiple error handlers can be chained, and each handler
    /// in the chain will be invoked when matching exceptions occur.
    /// </para>
    /// </remarks>
    /// <example>
    /// <para>Basic error handling with logging:</para>
    /// <code>
    /// ITaskScheduler scheduler = // ... obtain scheduler
    /// 
    /// var errorHandlingScheduler = scheduler
    ///     .WithOperationName("DatabaseOperation")
    ///     .OnError&lt;SqlException&gt;((sched, ex, operationName) => {
    ///         _logger.LogError(ex, "Database error in operation {OperationName}", 
    ///             operationName?.OperationName ?? "Unknown");
    ///     })
    ///     .OnError&lt;TimeoutException&gt;(ex => {
    ///         _metrics.IncrementCounter("operation.timeout");
    ///     });
    /// 
    /// try 
    /// {
    ///     await errorHandlingScheduler.Enqueue(() => DatabaseQueryAsync());
    /// }
    /// catch (SqlException ex)
    /// {
    ///     // Exception was logged by error handler, then re-thrown
    ///     // Handle the exception or let it propagate further
    /// }
    /// </code>
    /// <para>Custom annotation integration:</para>
    /// <code>
    /// public class UserContextAnnotation : IOperationAnnotation
    /// {
    ///     public string UserId { get; set; }
    ///     public string Role { get; set; }
    /// }
    /// 
    /// var contextualErrorScheduler = scheduler
    ///     .WithExtendedState(new UserContextAnnotation { UserId = "user123", Role = "admin" })
    ///     .OnError&lt;SecurityException, UserContextAnnotation&gt;((sched, ex, userContext) => {
    ///         _auditLog.LogSecurityViolation(
    ///             userId: userContext?.UserId ?? "unknown",
    ///             role: userContext?.Role ?? "unknown",
    ///             exception: ex);
    ///     });
    /// </code>
    /// <para>Reactive error handling with compensating actions:</para>
    /// <code>
    /// var reactiveScheduler = scheduler
    ///     .WithOperationName("PaymentProcessing")
    ///     .OnError&lt;PaymentFailedException&gt;((errorScheduler, ex) => {
    ///         // Enqueue compensating action on the same scheduler
    ///         _ = errorScheduler.Enqueue(() => RefundPaymentAsync(ex.PaymentId));
    ///     })
    ///     .OnError&lt;Exception&gt;(ex => {
    ///         // Log all other exceptions
    ///         _logger.LogError(ex, "Unexpected error in payment processing");
    ///     });
    /// </code>
    /// </example>
    public static class ExceptionTaskSchedulerExtensions
    {
        /// <summary>
        /// Creates a task scheduler wrapper that handles exceptions of the specified type with access to the scheduler and operation name annotation.
        /// </summary>
        /// <typeparam name="TException">The type of exception to handle. Must derive from <see cref="Exception"/>.</typeparam>
        /// <param name="taskScheduler">The task scheduler to wrap with error handling.</param>
        /// <param name="errorAction">The action to execute when an exception of type <typeparamref name="TException"/> occurs. Receives the scheduler instance, the exception, and the operation name annotation if available.</param>
        /// <param name="errorFilter">An optional predicate to filter which exceptions should be handled. If <c>null</c>, all exceptions of the specified type are handled.</param>
        /// <returns>An <see cref="ITaskScheduler"/> that handles exceptions according to the specified parameters.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <remarks>
        /// This overload specifically provides access to <see cref="OperationNameAnnotation"/> in the error handler,
        /// making it convenient for scenarios where operation names are used for error logging and diagnostics.
        /// The error handler receives the scheduler instance, allowing for reactive error handling patterns.
        /// </remarks>
        public static ITaskScheduler OnError<TException>(this ITaskScheduler taskScheduler, Action<ITaskScheduler, TException> errorAction, Func<TException, bool>? errorFilter = null)
            where TException : Exception
        {
            return new AnnotatedExceptionTaskSchedulerWrapper<TException, IOperationAnnotation>(taskScheduler, errorFilter ?? DefaultErrorFilter, (scheduler, exception, _) => errorAction(scheduler, exception));
        }

        /// <summary>
        /// Creates a task scheduler wrapper that handles exceptions of the specified type with a simple error action.
        /// </summary>
        /// <typeparam name="TException">The type of exception to handle. Must derive from <see cref="Exception"/>.</typeparam>
        /// <param name="taskScheduler">The task scheduler to wrap with error handling.</param>
        /// <param name="errorAction">The action to execute when an exception of type <typeparamref name="TException"/> occurs. Receives only the exception instance.</param>
        /// <param name="errorFilter">An optional predicate to filter which exceptions should be handled. If <c>null</c>, all exceptions of the specified type are handled.</param>
        /// <returns>An <see cref="ITaskScheduler"/> that handles exceptions according to the specified parameters.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> or <paramref name="errorAction"/> is <c>null</c>.</exception>
        /// <remarks>
        /// This is the simplest error handling overload, suitable for basic error logging or notification scenarios
        /// where scheduler access and annotation context are not needed.
        /// </remarks>
        public static ITaskScheduler OnError<TException>(this ITaskScheduler taskScheduler, Action<TException> errorAction, Func<TException, bool>? errorFilter = null)
            where TException : Exception
        {
            Argument.NotNull(errorAction);

            return new AnnotatedExceptionTaskSchedulerWrapper<TException, IOperationAnnotation>(taskScheduler, errorFilter ?? DefaultErrorFilter, (_, exception, _) => errorAction(exception));
        }

        /// <summary>
        /// Creates a task scheduler wrapper that handles all exceptions with access to the scheduler instance.
        /// </summary>
        /// <param name="taskScheduler">The task scheduler to wrap with error handling.</param>
        /// <param name="errorAction">The action to execute when any exception occurs. Receives the scheduler instance and the exception.</param>
        /// <returns>An <see cref="ITaskScheduler"/> that handles all exceptions.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <remarks>
        /// This overload handles all exception types derived from <see cref="Exception"/>, making it suitable
        /// for general error logging or fallback error handling scenarios.
        /// </remarks>
        public static ITaskScheduler OnError(this ITaskScheduler taskScheduler, Action<ITaskScheduler, Exception> errorAction)
        {
            return OnError<Exception>(taskScheduler, errorAction);
        }

        /// <summary>
        /// Creates a task scheduler wrapper that handles all exceptions with a simple error action.
        /// </summary>
        /// <param name="taskScheduler">The task scheduler to wrap with error handling.</param>
        /// <param name="errorAction">The action to execute when any exception occurs. Receives only the exception instance.</param>
        /// <returns>An <see cref="ITaskScheduler"/> that handles all exceptions.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <remarks>
        /// This is the most general error handling overload, suitable for basic error logging scenarios
        /// where specific exception types and operation context are not important.
        /// </remarks>
        public static ITaskScheduler OnError(this ITaskScheduler taskScheduler, Action<Exception> errorAction)
        {
            return OnError<Exception>(taskScheduler, errorAction);
        }

        /// <summary>
        /// Creates a task scheduler wrapper that handles exceptions of the specified type with access to operation name annotations.
        /// </summary>
        /// <typeparam name="TException">The type of exception to handle. Must derive from <see cref="Exception"/>.</typeparam>
        /// <param name="taskScheduler">The task scheduler to wrap with error handling.</param>
        /// <param name="errorAction">The action to execute when an exception occurs. Receives the scheduler, exception, and operation name annotation.</param>
        /// <param name="errorFilter">An optional predicate to filter which exceptions should be handled. If <c>null</c>, all exceptions of the specified type are handled.</param>
        /// <returns>An <see cref="ITaskScheduler"/> that handles exceptions with operation name context.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <remarks>
        /// This overload explicitly provides <see cref="OperationNameAnnotation"/> access, making it ideal for
        /// scenarios where operation names are consistently used and needed for error context.
        /// </remarks>
        public static ITaskScheduler OnError<TException>(this ITaskScheduler taskScheduler, Action<ITaskScheduler, TException, OperationNameAnnotation?> errorAction, Func<TException, bool>? errorFilter = null)
            where TException : Exception
        {
            return new AnnotatedExceptionTaskSchedulerWrapper<TException, OperationNameAnnotation>(taskScheduler, errorFilter ?? DefaultErrorFilter, errorAction);
        }

        /// <summary>
        /// Creates a task scheduler wrapper that handles exceptions of the specified type with access to custom operation annotations.
        /// </summary>
        /// <typeparam name="TException">The type of exception to handle. Must derive from <see cref="Exception"/>.</typeparam>
        /// <typeparam name="TAnnotation">The type of operation annotation to provide to the error handler. Must implement <see cref="IOperationAnnotation"/>.</typeparam>
        /// <param name="taskScheduler">The task scheduler to wrap with error handling.</param>
        /// <param name="errorAction">The action to execute when an exception occurs. Receives the scheduler, exception, and custom annotation.</param>
        /// <param name="errorFilter">An optional predicate to filter which exceptions should be handled. If <c>null</c>, all exceptions of the specified type are handled.</param>
        /// <returns>An <see cref="ITaskScheduler"/> that handles exceptions with custom annotation context.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <remarks>
        /// This is the most flexible error handling overload, allowing access to any custom annotation type
        /// that implements <see cref="IOperationAnnotation"/>. This enables rich contextual error handling
        /// with application-specific metadata.
        /// </remarks>
        public static ITaskScheduler OnError<TException, TAnnotation>(this ITaskScheduler taskScheduler, Action<ITaskScheduler, TException, TAnnotation?> errorAction, Func<TException, bool>? errorFilter = null)
            where TException : Exception
            where TAnnotation : IOperationAnnotation
        {
            return new AnnotatedExceptionTaskSchedulerWrapper<TException, TAnnotation>(taskScheduler, errorFilter ?? DefaultErrorFilter, errorAction);
        }

        private static bool DefaultErrorFilter<TException>(TException _)
            where TException : Exception
        {
            return true;
        }

        private sealed class AnnotatedExceptionTaskSchedulerWrapper<TException, TAnnotation> : ITaskScheduler
            where TException : Exception
            where TAnnotation : IOperationAnnotation
        {
            private readonly ITaskScheduler _baseTaskScheduler;
            private readonly Func<TException, bool> _errorFilter;
            private readonly Action<ITaskScheduler, TException, TAnnotation?> _errorAction;

            public AnnotatedExceptionTaskSchedulerWrapper(
                ITaskScheduler baseTaskScheduler,
                Func<TException, bool> errorFilter,
                Action<ITaskScheduler, TException, TAnnotation?> errorAction)
            {
                Argument.NotNull(baseTaskScheduler);
                Argument.NotNull(errorFilter);
                Argument.NotNull(errorAction);

                _baseTaskScheduler = baseTaskScheduler;
                _errorFilter = errorFilter;
                _errorAction = errorAction;
            }

            public async Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
            {
                TAnnotation? annotation = default;
                if (!typeof(TAnnotation).IsInterface)
                {
                    annotation = (state as ExtendedState)
                        .Unwrap<TAnnotation>()
                        .FirstOrDefault();
                }

                try
                {
                    return await _baseTaskScheduler.Enqueue(taskFunc, state, cancellationToken).ConfigureAwait(false);
                }
                catch (TException exception) when (_errorFilter(exception))
                {
                    _errorAction(this, exception, annotation);
                    throw;
                }
            }
        }
    }
}