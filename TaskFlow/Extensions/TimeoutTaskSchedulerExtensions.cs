namespace System.Threading.Tasks.Flow
{
    using System.Globalization;
    using System.Linq;
    using System.Threading.Tasks.Flow.Annotations;

    /// <summary>
    /// Provides extension methods for <see cref="ITaskScheduler"/> to add timeout capabilities to task operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class implements timeout functionality for task scheduler operations, automatically cancelling
    /// operations that exceed a specified time limit. The timeout mechanism works by racing the actual
    /// operation against a delay task, cancelling whichever doesn't complete first.
    /// </para>
    /// <para>
    /// Key features of the timeout system:
    /// </para>
    /// <list type="bullet">
    ///   <item><strong>Automatic cancellation</strong> - Operations are cancelled if they exceed the timeout duration</item>
    ///   <item><strong>Operation name integration</strong> - Timeout exceptions include operation names when available</item>
    ///   <item><strong>Resource management</strong> - Properly cancels and cleans up both timeout and operation tasks</item>
    ///   <item><strong>Exception clarity</strong> - Provides clear timeout exception messages with contextual information</item>
    /// </list>
    /// <para>
    /// The timeout implementation uses a race condition between two tasks:
    /// </para>
    /// <list type="number">
    ///   <item>The actual operation task</item>
    ///   <item>A timeout delay task</item>
    /// </list>
    /// <para>
    /// Whichever task completes first determines the outcome. If the timeout task completes first,
    /// a <see cref="TimeoutException"/> is thrown and the operation task is cancelled. If the operation
    /// completes first, the timeout task is cancelled and the operation result is returned.
    /// </para>
    /// <para>
    /// The timeout clock starts when <c>Enqueue</c> reaches this wrapper, not when the delegate begins running.
    /// Time spent waiting in an underlying scheduler's queue therefore counts toward the timeout. On a built-in
    /// sequential flow, a timed-out queued delegate can later be invoked with an already-canceled token.
    /// Cancellation is cooperative, so a delegate that ignores its token may continue running.
    /// </para>
    /// <para>
    /// Common use cases include:
    /// </para>
    /// <list type="bullet">
    ///   <item>Protecting against hanging operations in external service calls</item>
    ///   <item>Enforcing SLA requirements for operation completion times</item>
    ///   <item>Preventing resource exhaustion from long-running operations</item>
    ///   <item>Implementing circuit breaker patterns with time-based failures</item>
    ///   <item>Adding timeout protection to legacy or unreliable code</item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <para>Basic timeout usage:</para>
    /// <code>
    /// ITaskScheduler scheduler = // ... obtain scheduler
    /// var timeoutScheduler = scheduler.WithTimeout(TimeSpan.FromSeconds(30));
    ///
    /// async Task&lt;string&gt; RunAsync(CancellationToken token)
    /// {
    ///     await SomeLongRunningOperationAsync(token);
    ///     return "completed";
    /// }
    /// 
    /// try 
    /// {
    ///     // Queue wait and delegate execution share the same 30-second budget
    ///     var result = await timeoutScheduler.Enqueue(RunAsync);
    ///     
    ///     Console.WriteLine($"Operation completed: {result}");
    /// }
    /// catch (TimeoutException ex)
    /// {
    ///     Console.WriteLine($"Operation timed out: {ex.Message}");
    /// }
    /// </code>
    /// <para>Timeout with operation naming for better error messages:</para>
    /// <code>
    /// var namedTimeoutScheduler = scheduler
    ///     .WithOperationName("DatabaseQuery")
    ///     .WithTimeout(TimeSpan.FromSeconds(15));
    /// 
    /// try 
    /// {
    ///     var data = await namedTimeoutScheduler.Enqueue(() => QueryDatabaseAsync());
    /// }
    /// catch (TimeoutException ex)
    /// {
    ///     // Exception message will include: "Operation `DatabaseQuery` has timed out in 00:00:15"
    ///     _logger.LogError(ex, "Database query exceeded timeout");
    /// }
    /// </code>
    /// <para>Chaining timeouts with other extensions:</para>
    /// <code>
    /// var robustScheduler = scheduler
    ///     .WithOperationName("ExternalApiCall")
    ///     .WithTimeout(TimeSpan.FromSeconds(10))
    ///     .OnError&lt;TimeoutException&gt;((sched, ex, name) => {
    ///         _metrics.IncrementCounter("api.timeout", 
    ///             new { operation = name?.OperationName ?? "unknown" });
    ///     })
    ///     .OnError&lt;HttpRequestException&gt;(ex => {
    ///         _logger.LogWarning(ex, "HTTP error in API call");
    ///     });
    /// 
    /// // This operation has comprehensive error handling and timeout protection
    /// var result = await robustScheduler.Enqueue(() => CallExternalApiAsync());
    /// </code>
    /// <para>Different timeout scenarios:</para>
    /// <code>
    /// // Short timeout for quick operations
    /// var quickScheduler = scheduler.WithTimeout(TimeSpan.FromSeconds(5));
    /// 
    /// // Longer timeout for complex operations  
    /// var complexScheduler = scheduler.WithTimeout(TimeSpan.FromMinutes(2));
    /// 
    /// // Infinite timeout (effectively disables timeout)
    /// var infiniteScheduler = scheduler.WithTimeout(Timeout.InfiniteTimeSpan);
    /// 
    /// // Very precise timeout
    /// var preciseScheduler = scheduler.WithTimeout(TimeSpan.FromMilliseconds(500));
    /// </code>
    /// </example>
    public static class TimeoutTaskSchedulerExtensions
    {
        /// <summary>
        /// Creates a task scheduler wrapper that automatically cancels operations that exceed the specified timeout duration.
        /// </summary>
        /// <param name="taskScheduler">The base task scheduler to wrap with timeout functionality.</param>
        /// <param name="timeout">The maximum time to allow for operation completion. Must be greater than <see cref="TimeSpan.Zero"/> and not exceed <see cref="int.MaxValue"/> milliseconds, or be equal to <see cref="Timeout.InfiniteTimeSpan"/>.</param>
        /// <returns>An <see cref="ITaskScheduler"/> that enforces the specified timeout on all operations.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="timeout"/> is not a valid timeout value.</exception>
        /// <remarks>
        /// <para>
        /// This method starts a timeout race when <c>Enqueue</c> reaches the wrapper, before the delegate necessarily
        /// starts on the base scheduler. Queue wait and delegate execution consume the same budget. The implementation
        /// uses <see cref="Internal.TaskExtensions.WhenAnyCancelRest"/>
        /// to ensure that when one task completes, the other is properly cancelled and cleaned up.
        /// </para>
        /// <para>
        /// Timeout behavior characteristics:
        /// </para>
        /// <list type="bullet">
        ///   <item><strong>Race condition</strong> - The first task to complete (operation or timeout) determines the outcome</item>
        ///   <item><strong>Automatic cancellation</strong> - When timeout occurs, the losing operation receives a cooperative cancellation signal</item>
        ///   <item><strong>Queue delay</strong> - Time spent waiting for the base scheduler counts toward the timeout</item>
        ///   <item><strong>Exception handling</strong> - Timeout results in a <see cref="TimeoutException"/> with descriptive message</item>
        ///   <item><strong>Cancellation propagation</strong> - Original cancellation tokens are respected in addition to timeout cancellation</item>
        /// </list>
        /// <para>
        /// The timeout value validation ensures:
        /// </para>
        /// <list type="bullet">
        ///   <item>Positive timeout values are within the valid range for <see cref="Task.Delay(TimeSpan, CancellationToken)"/></item>
        ///   <item>Zero or negative values are rejected as invalid</item>
        /// </list>
        /// <para>
        /// Exception message formatting includes operation context when available:
        /// </para>
        /// <list type="bullet">
        ///   <item>If an <see cref="OperationNameAnnotation"/> is present, the message includes the operation name</item>
        ///   <item>If no operation name is available, a generic timeout message is used</item>
        ///   <item>The timeout duration is always included in the message for diagnostics</item>
        /// </list>
        /// <para>
        /// The wrapper maintains all characteristics of the base scheduler (execution order, concurrency
        /// behavior, etc.) while adding timeout protection. Operations that complete within the timeout
        /// are returned normally with no additional overhead.
        /// </para>
        /// <para>
        /// Performance considerations:
        /// </para>
        /// <list type="bullet">
        ///   <item>Each operation creates an additional timeout task that runs concurrently</item>
        ///   <item>Task cancellation and cleanup overhead when timeout occurs</item>
        ///   <item>Minimal overhead for operations that complete within timeout</item>
        ///   <item>Memory overhead proportional to the number of concurrent timeout operations</item>
        /// </list>
        /// </remarks>
        /// <example>
        /// <code>
        /// ITaskScheduler baseScheduler = new TaskFlow();
        /// 
        /// // Create scheduler with a 30-second queue-and-execution budget
        /// var timeoutScheduler = baseScheduler.WithTimeout(TimeSpan.FromSeconds(30));
        ///
        /// async Task&lt;string&gt; RunAsync(CancellationToken token)
        /// {
        ///     for (int i = 0; i &lt; 100; i++)
        ///     {
        ///         token.ThrowIfCancellationRequested();
        ///         await Task.Delay(500, token);
        ///     }
        ///     return "completed";
        /// }
        /// 
        /// try 
        /// {
        ///     var result = await timeoutScheduler.Enqueue(RunAsync);
        /// }
        /// catch (TimeoutException ex)
        /// {
        ///     Console.WriteLine($"Operation exceeded 30 second timeout: {ex.Message}");
        /// }
        /// catch (OperationCanceledException ex) when (ex is not TimeoutException)
        /// {
        ///     Console.WriteLine("Operation was cancelled for other reasons");
        /// }
        /// </code>
        /// </example>
        public static ITaskScheduler WithTimeout(this ITaskScheduler taskScheduler, TimeSpan timeout)
        {
            Argument.NotNull(taskScheduler);
            return taskScheduler.UseMiddleware(new TimeoutMiddleware(timeout));
        }

        private sealed class TimeoutMiddleware : ITaskSchedulerEnqueueMiddleware
        {
            private readonly TimeSpan _timeout;

            public TimeoutMiddleware(TimeSpan timeout)
            {
                Argument.Assert(timeout, t => t > TimeSpan.Zero && t.TotalMilliseconds <= int.MaxValue || t == Timeout.InfiniteTimeSpan, "Wrong timeout value");
                _timeout = timeout;
            }

            public async Task<TResult> InvokeAsync<TResult>(TaskSchedulerEnqueueContext<TResult> context, TaskSchedulerEnqueueDelegate<TResult> continuation)
            {
                return await Internal.TaskExtensions.WhenAnyCancelRest(new[] { TimeoutAsync, EnqueueInternalAsync }, context.CancellationToken).ConfigureAwait(false);

                async Task<TResult> TimeoutAsync(CancellationToken token)
                {
                    await Task.Delay(_timeout, token).ConfigureAwait(false);
                    throw new TimeoutException(FormatExceptionMessage());
                }

                Task<TResult> EnqueueInternalAsync(CancellationToken token)
                {
                    return continuation(context.WithCancellationToken(token));
                }

                string FormatExceptionMessage()
                {
                    var operationName = context.GetAnnotation<OperationNameAnnotation>()?.OperationName;

                    return operationName == null
                        ? string.Format(CultureInfo.InvariantCulture, "Operation has timed out in {0}", _timeout)
                        : string.Format(CultureInfo.InvariantCulture, "Operation `{0}` has timed out in {1}", operationName, _timeout);
                }
            }
        }
    }
}
