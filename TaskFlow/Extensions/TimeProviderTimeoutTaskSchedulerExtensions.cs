namespace System.Threading.Tasks.Flow;

using System.Globalization;
using System.Threading.Tasks.Flow.Annotations;

/// <summary>
/// Provides timeout delays controlled by a <see cref="TimeProvider"/>.
/// </summary>
#if !TASKFLOW_EXTENSIONS_TIME
[System.Diagnostics.CodeAnalysis.SuppressMessage("ApiDesign", "RS0016:Add public types and members to the declared API", Justification = "This API is provided by TaskFlow.Extensions.Time for netstandard2.0 consumers.")]
#endif
public static class TimeProviderTimeoutTaskSchedulerExtensions
{
    /// <summary>
    /// Creates a scheduler wrapper with a queue-and-execution timeout measured by the specified time provider.
    /// </summary>
    /// <param name="taskScheduler">The scheduler to wrap.</param>
    /// <param name="timeout">The timeout budget, or <see cref="Timeout.InfiniteTimeSpan"/>.</param>
    /// <param name="timeProvider">The provider used for timeout delays. If <c>null</c>, <see cref="TimeProvider.System"/> is used.</param>
    /// <returns>A scheduler that cancels operations and throws <see cref="TimeoutException"/> when the budget expires.</returns>
    /// <exception cref="ArgumentNullException">The scheduler is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">The timeout is not positive or infinite, or exceeds <see cref="int.MaxValue"/> milliseconds.</exception>
    public static ITaskScheduler WithTimeout(this ITaskScheduler taskScheduler, TimeSpan timeout, TimeProvider? timeProvider)
    {
        Argument.NotNull(taskScheduler);
        return taskScheduler.UseMiddleware(new TimeoutMiddleware(timeout, timeProvider ?? TimeProvider.System));
    }

    private sealed class TimeoutMiddleware : ITaskSchedulerEnqueueMiddleware
    {
        private readonly TimeSpan _timeout;

        private readonly TimeProvider _timeProvider;

        public TimeoutMiddleware(TimeSpan timeout, TimeProvider timeProvider)
        {
            Argument.Assert(timeout, t => t > TimeSpan.Zero && t.TotalMilliseconds <= int.MaxValue || t == Timeout.InfiniteTimeSpan, "Wrong timeout value");
            _timeout = timeout;
            _timeProvider = timeProvider;
        }

        public async Task<TResult> InvokeAsync<TResult>(TaskSchedulerEnqueueContext<TResult> context, TaskSchedulerEnqueueDelegate<TResult> continuation)
        {
            return await Internal.TaskExtensions.WhenAnyCancelRest(new[] { TimeoutAsync, EnqueueInternalAsync }, context.CancellationToken).ConfigureAwait(false);

            async Task<TResult> TimeoutAsync(CancellationToken token)
            {
#if NET8_0_OR_GREATER
                await Task.Delay(_timeout, _timeProvider, token).ConfigureAwait(false);
#else
                await TimeProviderTaskExtensions.Delay(_timeProvider, _timeout, token).ConfigureAwait(false);
#endif

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