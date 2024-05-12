namespace System.Threading.Tasks.Flow
{
    using System.Globalization;
    using System.Linq;
    using System.Threading.Tasks.Flow.Annotations;

    public static class TimeoutTaskSchedulerExtensions
    {
        public static ITaskScheduler WithTimeout(this ITaskScheduler taskScheduler, TimeSpan timeout)
        {
            return new TimeoutTaskSchedulerWrapper(taskScheduler, timeout);
        }

        private sealed class TimeoutTaskSchedulerWrapper : ITaskScheduler
        {
            private readonly ITaskScheduler _baseTaskScheduler;
            private readonly TimeSpan _timeout;

            public TimeoutTaskSchedulerWrapper(ITaskScheduler baseTaskScheduler, TimeSpan timeout)
            {
                Argument.NotNull(baseTaskScheduler);
                Argument.Assert(timeout, t => t > TimeSpan.Zero && t.TotalMilliseconds <= int.MaxValue || t == Timeout.InfiniteTimeSpan, "Wrong timeout value");

                _baseTaskScheduler = baseTaskScheduler;
                _timeout = timeout;
            }

            public async Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
            {
                return await Internal.TaskExtensions.WhenAnyCancelRest(new[] { TimeoutAsync, EnqueueInternalAsync }, cancellationToken).ConfigureAwait(false);

                async Task<T> TimeoutAsync(CancellationToken token)
                {
                    await Task.Delay(_timeout, token).ConfigureAwait(false);
                    throw new TimeoutException(FormatExceptionMessage());
                }

                async Task<T> EnqueueInternalAsync(CancellationToken token)
                {
                    return await _baseTaskScheduler.Enqueue(taskFunc, state, token).ConfigureAwait(false);
                }

                string FormatExceptionMessage()
                {
                    var operationName = (state as ExtendedState)
                        .Unwrap<OperationAnnotation>()
                        .FirstOrDefault(annotation => annotation.OperationName != null)?.OperationName;

                    return operationName == null
                        ? string.Format(CultureInfo.InvariantCulture, "Operation has timed out in {0}", _timeout)
                        : string.Format(CultureInfo.InvariantCulture, "Operation `{0}` has timed out in {1}", operationName, _timeout);
                }
            }
        }
    }
}
