namespace System.Threading.Tasks.Flow
{
    using System.Threading.Tasks.Flow.Annotations;

    /// <summary>
    /// Provides extension methods for adding admission-based throttling to an <see cref="ITaskScheduler"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="WithThrottle(ITaskScheduler, TimeSpan, TimeProvider?)"/> implements leading-edge throttling.
    /// It admits the first operation and rejects later submissions until the configured interval has elapsed.
    /// </para>
    /// <para>
    /// The interval begins when a submission is admitted by this wrapper, before the underlying scheduler
    /// invokes its delegate. Consequently, an admitted operation consumes the interval even if it later fails
    /// or is canceled. Rejected operations are not queued, delayed, or replaced by a later submission.
    /// </para>
    /// </remarks>
    /// <example>
    /// <para>Rejecting submissions that arrive too frequently:</para>
    /// <code>
    /// ITaskScheduler scheduler = // ... obtain scheduler
    /// var throttledScheduler = scheduler.WithThrottle(TimeSpan.FromSeconds(2));
    /// 
    /// // Simulate rapid document changes
    /// for (int i = 0; i &lt; 5; i++)
    /// {
    ///     try 
    ///     {
    ///         await throttledScheduler.Enqueue(() => SaveDocumentAsync());
    ///         Console.WriteLine($"Save {i + 1} completed");
    ///     }
    ///     catch (OperationThrottledException ex)
    ///     {
    ///         Console.WriteLine($"Save {i + 1} throttled: {ex.Message}");
    ///     }
    ///     
    ///     await Task.Delay(500); // Wait 500ms between attempts
    /// }
    /// // Only the first submission and submissions after 2+ second intervals are admitted
    /// </code>
    /// <para>Custom time provider for testing:</para>
    /// <code>
    /// // Using FakeTimeProvider for unit testing
    /// var fakeTimeProvider = new FakeTimeProvider();
    /// var testScheduler = scheduler.WithThrottle(TimeSpan.FromMinutes(5), fakeTimeProvider);
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
        /// Creates a scheduler wrapper that admits at most one submission during each configured interval.
        /// </summary>
        /// <param name="taskScheduler">The base task scheduler to wrap with throttling functionality.</param>
        /// <param name="interval">The minimum interval between admitted submissions.</param>
        /// <param name="timeProvider">The time provider to use for timing measurements. If <c>null</c>, <see cref="TimeProvider.System"/> is used.</param>
        /// <returns>An <see cref="ITaskScheduler"/> that enforces the throttle interval between submissions.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="interval"/> is less than or equal to <see cref="TimeSpan.Zero"/>.</exception>
        /// <remarks>
        /// <para>
        /// This method tracks the timestamp of the last admitted submission. When a new operation is enqueued,
        /// the wrapper checks whether the specified interval has elapsed. If not enough time has passed, the operation is rejected with an
        /// <see cref="OperationThrottledException"/>.
        /// </para>
        /// <para>
        /// Throttle behavior characteristics:
        /// </para>
        /// <list type="bullet">
        ///   <item><strong>First submission</strong> - Admitted immediately</item>
        ///   <item><strong>Subsequent submissions</strong> - Admitted when elapsed time is greater than or equal to the interval</item>
        ///   <item><strong>Failed or canceled operations</strong> - Consume the interval because timing is recorded at admission</item>
        ///   <item><strong>Rejected submissions</strong> - Complete with <see cref="OperationThrottledException"/> without reaching the base scheduler</item>
        ///   <item><strong>Thread safety</strong> - Admission checks and timestamp updates are synchronized</item>
        /// </list>
        /// <para>
        /// The time provider parameter allows for custom timing implementations, which is particularly useful
        /// for unit testing scenarios where time needs to be controlled or accelerated. When <c>null</c> is
        /// provided, the system time provider is used for production scenarios.
        /// </para>
        /// <para>
        /// The wrapper maintains all characteristics of the base scheduler (execution order, concurrency
        /// behavior, etc.) while adding throttling. Operations that pass the throttle
        /// check are forwarded to the base scheduler unchanged.
        /// </para>
        /// This is not trailing-edge debounce: it does not wait for a quiet interval or eventually run the latest rejected submission.
        /// </remarks>
        /// <example>
        /// <code>
        /// ITaskScheduler baseScheduler = new TaskFlow();
        /// 
        /// // Create a leading-edge throttle with a 1-second admission interval
        /// var throttledScheduler = baseScheduler.WithThrottle(TimeSpan.FromSeconds(1));
        /// 
        /// // First operation executes immediately
        /// await throttledScheduler.Enqueue(() => Console.WriteLine("Operation 1"));
        /// 
        /// try 
        /// {
        ///     // This will be throttled since less than 1 second has passed
        ///     await throttledScheduler.Enqueue(() => Console.WriteLine("Operation 2"));
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
        /// await throttledScheduler.Enqueue(() => Console.WriteLine("Operation 3"));
        /// </code>
        /// </example>
        public static ITaskScheduler WithThrottle(this ITaskScheduler taskScheduler, TimeSpan interval, TimeProvider? timeProvider = null)
        {
            Argument.NotNull(taskScheduler);
            return taskScheduler.UseMiddleware(new ThrottleMiddleware(timeProvider ?? TimeProvider.System, interval));
        }

        private sealed class ThrottleMiddleware : ITaskSchedulerEnqueueMiddleware
        {
            private readonly TimeProvider _timeProvider;
            private readonly TimeSpan _interval;
            private readonly object _lastAdmissionLock;

            private long _lastAdmissionTimestamp;
            private bool _hasAdmission;

            public ThrottleMiddleware(TimeProvider timeProvider, TimeSpan interval)
            {
                Argument.NotNull(timeProvider);
                Argument.Assert(interval, ts => ts > TimeSpan.Zero, "Interval should be greater than zero");

                _interval = interval;
                _timeProvider = timeProvider;
                _lastAdmissionLock = new object();
            }

            public Task<TResult> InvokeAsync<TResult>(TaskSchedulerEnqueueContext<TResult> context, TaskSchedulerEnqueueDelegate<TResult> continuation)
            {
                lock (_lastAdmissionLock)
                {
                    var currentTimestamp = _timeProvider.GetTimestamp();
                    var elapsed = _timeProvider.GetElapsedTime(_lastAdmissionTimestamp, currentTimestamp);
                    if (_hasAdmission && elapsed < _interval)
                    {
                        throw new OperationThrottledException($"Operation did not execute because the throttle interval has not elapsed. Interval: {_interval}. Elapsed: {elapsed}.");
                    }

                    _lastAdmissionTimestamp = currentTimestamp;
                    _hasAdmission = true;
                }

                return continuation(context);
            }
        }
    }
}
