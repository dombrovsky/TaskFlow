namespace System.Threading.Tasks.Flow
{
    using System.Threading.Tasks.Flow.Annotations;

    /// <summary>
    /// Provides extension methods for <see cref="ITaskScheduler"/> to add throttling and debouncing capabilities.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class implements throttling mechanisms that control the rate at which operations can be executed
    /// on a task scheduler. The primary implementation is a debounce pattern that enforces a minimum time
    /// interval between successful operation executions.
    /// </para>
    /// <para>
    /// Throttling behaviors provided:
    /// </para>
    /// <list type="bullet">
    ///   <item><strong>Debouncing</strong> - Enforces a minimum interval between operations, rejecting operations that are enqueued too soon after the previous execution</item>
    ///   <item><strong>Time-based throttling</strong> - Uses configurable time providers for precise timing control</item>
    ///   <item><strong>Exception-based rejection</strong> - Throws <see cref="OperationThrottledException"/> when operations are throttled</item>
    ///   <item><strong>Thread-safe operation</strong> - Multiple threads can safely enqueue operations with proper synchronization</item>
    /// </list>
    /// <para>
    /// Common use cases for throttling include:
    /// </para>
    /// <list type="bullet">
    ///   <item>Auto-save functionality with debouncing to prevent excessive file writes</item>
    ///   <item>Search suggestion throttling to reduce server requests</item>
    ///   <item>Rate limiting for external API calls</item>
    ///   <item>UI update throttling to prevent excessive rendering</item>
    ///   <item>Resource protection by limiting operation frequency</item>
    /// </list>
    /// <para>
    /// The throttling implementation tracks the timestamp of the last successful operation execution
    /// and compares it with the current time when new operations are enqueued. If the elapsed time
    /// is less than the configured interval, the operation is rejected with an <see cref="OperationThrottledException"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <para>Basic debounce implementation for auto-save:</para>
    /// <code>
    /// ITaskScheduler scheduler = // ... obtain scheduler
    /// var debouncedScheduler = scheduler.WithDebounce(TimeSpan.FromSeconds(2));
    /// 
    /// // Simulate rapid document changes
    /// for (int i = 0; i &lt; 5; i++)
    /// {
    ///     try 
    ///     {
    ///         await debouncedScheduler.Enqueue(() => SaveDocumentAsync());
    ///         Console.WriteLine($"Save {i + 1} completed");
    ///     }
    ///     catch (OperationThrottledException ex)
    ///     {
    ///         Console.WriteLine($"Save {i + 1} throttled: {ex.Message}");
    ///     }
    ///     
    ///     await Task.Delay(500); // Wait 500ms between attempts
    /// }
    /// // Only the first save and saves after 2+ second intervals will succeed
    /// </code>
    /// <para>Search debouncing with error handling:</para>
    /// <code>
    /// var searchScheduler = scheduler.WithDebounce(TimeSpan.FromMilliseconds(300));
    /// 
    /// async Task HandleSearchInput(string query)
    /// {
    ///     try 
    ///     {
    ///         var results = await searchScheduler.Enqueue(async () => {
    ///             return await SearchApiAsync(query);
    ///         });
    ///         
    ///         DisplaySearchResults(results);
    ///     }
    ///     catch (OperationThrottledException)
    ///     {
    ///         // Search was throttled - this is expected behavior
    ///         // No action needed as the user is still typing
    ///     }
    /// }
    /// 
    /// // User typing "hello" quickly:
    /// await HandleSearchInput("h");     // Executes
    /// await HandleSearchInput("he");    // Throttled
    /// await HandleSearchInput("hel");   // Throttled  
    /// await HandleSearchInput("hell");  // Throttled
    /// await HandleSearchInput("hello"); // Throttled
    /// // Only "h" search executes; if user pauses &gt; 300ms, next search will execute
    /// </code>
    /// <para>Custom time provider for testing:</para>
    /// <code>
    /// // Using FakeTimeProvider for unit testing
    /// var fakeTimeProvider = new FakeTimeProvider();
    /// var testScheduler = scheduler.WithDebounce(TimeSpan.FromMinutes(5), fakeTimeProvider);
    /// 
    /// // First operation should succeed
    /// await testScheduler.Enqueue(() => SomeOperation());
    /// 
    /// // Advance time by 4 minutes - next operation should be throttled
    /// fakeTimeProvider.Advance(TimeSpan.FromMinutes(4));
    /// await Assert.ThrowsAsync&lt;OperationThrottledException&gt;(() => 
    ///     testScheduler.Enqueue(() => SomeOperation()));
    /// 
    /// // Advance time by 2 more minutes (6 minutes total) - next operation should succeed
    /// fakeTimeProvider.Advance(TimeSpan.FromMinutes(2));
    /// await testScheduler.Enqueue(() => SomeOperation()); // Should succeed
    /// </code>
    /// </example>
    public static class ThrottlingTaskSchedulerExtensions
    {
        /// <summary>
        /// Creates a task scheduler wrapper that implements debouncing by enforcing a minimum time interval between operation executions.
        /// </summary>
        /// <param name="taskScheduler">The base task scheduler to wrap with debouncing functionality.</param>
        /// <param name="interval">The minimum time interval that must elapse between successful operation executions.</param>
        /// <param name="timeProvider">The time provider to use for timing measurements. If <c>null</c>, <see cref="TimeProvider.System"/> is used.</param>
        /// <returns>An <see cref="ITaskScheduler"/> that enforces the debounce interval between operations.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="interval"/> is less than or equal to <see cref="TimeSpan.Zero"/>.</exception>
        /// <remarks>
        /// <para>
        /// This method creates a wrapper that tracks the timestamp of the last successful operation execution.
        /// When a new operation is enqueued, the wrapper checks if the specified interval has elapsed since
        /// the last execution. If not enough time has passed, the operation is immediately rejected with an
        /// <see cref="OperationThrottledException"/>.
        /// </para>
        /// <para>
        /// Debounce behavior characteristics:
        /// </para>
        /// <list type="bullet">
        ///   <item><strong>First operation</strong> - Always allowed to execute immediately</item>
        ///   <item><strong>Subsequent operations</strong> - Only allowed if the interval has elapsed since the last successful execution</item>
        ///   <item><strong>Failed operations</strong> - Do not count toward the interval timing (only successful completions are tracked)</item>
        ///   <item><strong>Cancelled operations</strong> - Do not count toward the interval timing</item>
        ///   <item><strong>Thread safety</strong> - Multiple threads can safely enqueue operations with proper synchronization</item>
        /// </list>
        /// <para>
        /// The time provider parameter allows for custom timing implementations, which is particularly useful
        /// for unit testing scenarios where time needs to be controlled or accelerated. When <c>null</c> is
        /// provided, the system time provider is used for production scenarios.
        /// </para>
        /// <para>
        /// The wrapper maintains all characteristics of the base scheduler (execution order, concurrency
        /// behavior, etc.) while adding the debouncing functionality. Operations that pass the debounce
        /// check are forwarded to the base scheduler unchanged.
        /// </para>
        /// <para>
        /// Performance considerations:
        /// </para>
        /// <list type="bullet">
        ///   <item>Each enqueue operation requires a timestamp comparison (very fast)</item>
        ///   <item>Thread synchronization overhead for the timestamp check</item>
        ///   <item>Rejected operations fail immediately without queuing overhead</item>
        ///   <item>Memory overhead is minimal (single timestamp and lock object)</item>
        /// </list>
        /// </remarks>
        /// <example>
        /// <code>
        /// ITaskScheduler baseScheduler = new TaskFlow();
        /// 
        /// // Create debounced scheduler with 1-second interval
        /// var debouncedScheduler = baseScheduler.WithDebounce(TimeSpan.FromSeconds(1));
        /// 
        /// // First operation executes immediately
        /// await debouncedScheduler.Enqueue(() => Console.WriteLine("Operation 1"));
        /// 
        /// try 
        /// {
        ///     // This will be throttled since less than 1 second has passed
        ///     await debouncedScheduler.Enqueue(() => Console.WriteLine("Operation 2"));
        /// }
        /// catch (OperationThrottledException ex)
        /// {
        ///     Console.WriteLine($"Throttled: {ex.Message}");
        /// }
        /// 
        /// // Wait for interval to pass
        /// await Task.Delay(TimeSpan.FromSeconds(1.1));
        /// 
        /// // This operation will execute since interval has elapsed
        /// await debouncedScheduler.Enqueue(() => Console.WriteLine("Operation 3"));
        /// </code>
        /// </example>
        public static ITaskScheduler WithDebounce(this ITaskScheduler taskScheduler, TimeSpan interval, TimeProvider? timeProvider = null)
        {
            return new DebounceTaskSchedulerWrapper(taskScheduler, timeProvider ?? TimeProvider.System, interval);
        }

        private sealed class DebounceTaskSchedulerWrapper : ITaskScheduler
        {
            private readonly ITaskScheduler _baseTaskScheduler;
            private readonly TimeProvider _timeProvider;
            private readonly TimeSpan _interval;
            private readonly object _lastExecutionLock;

            private long _lastExecutionTimestamp;

            public DebounceTaskSchedulerWrapper(ITaskScheduler baseTaskScheduler, TimeProvider timeProvider, TimeSpan interval)
            {
                Argument.NotNull(baseTaskScheduler);
                Argument.NotNull(timeProvider);
                Argument.Assert(interval, ts => ts > TimeSpan.Zero, "Interval should be greater than zero");

                _baseTaskScheduler = baseTaskScheduler;
                _interval = interval;
                _timeProvider = timeProvider;
                _lastExecutionLock = new object();
            }

            public async Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
            {
                var currentTimestamp = _timeProvider.GetTimestamp();

                lock (_lastExecutionLock)
                {
                    var elapsed = _timeProvider.GetElapsedTime(_lastExecutionTimestamp);
                    if (_lastExecutionTimestamp > 0 && elapsed <= _interval)
                    {
                        throw new OperationThrottledException($"Operation did not execute due to debounce interval not elapsed. Interval: {_interval}. Elapsed: {elapsed}.");
                    }

                    _lastExecutionTimestamp = currentTimestamp;
                }

                return await _baseTaskScheduler.Enqueue(taskFunc, state, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}