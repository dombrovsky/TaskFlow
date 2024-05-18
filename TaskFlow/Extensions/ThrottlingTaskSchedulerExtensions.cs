namespace System.Threading.Tasks.Flow
{
    using System.Threading.Tasks.Flow.Annotations;

    public static class ThrottlingTaskSchedulerExtensions
    {
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