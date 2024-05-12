namespace System.Threading.Tasks.Flow
{
    using System.Threading.Tasks.Flow.Annotations;
    using System.Threading.Tasks.Flow.Internal;

    public static class CancelPreviousTaskSchedulerExtensions
    {
        public static ITaskScheduler CreateCancelPrevious(this ITaskScheduler taskScheduler)
        {
            return new CancelPreviousTaskSchedulerWrapper(taskScheduler);
        }

        private sealed class CancelPreviousTaskSchedulerWrapper : ITaskScheduler
        {
            private readonly ITaskScheduler _baseTaskScheduler;
            private readonly CancelAllTokensAllocator _cancelAllTokensAllocator;

            public CancelPreviousTaskSchedulerWrapper(ITaskScheduler baseTaskScheduler)
            {
                Argument.NotNull(baseTaskScheduler);

                _baseTaskScheduler = baseTaskScheduler;
                _cancelAllTokensAllocator = new CancelAllTokensAllocator();
            }

            public async Task<T> Enqueue<T>(Func<CancellationToken, ValueTask<T>> taskFunc, CancellationToken cancellationToken)
            {
                _cancelAllTokensAllocator.Cancel();

                using (_cancelAllTokensAllocator.AllocateCancellationToken(out var allocatedToken))
                {
                    using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, allocatedToken);
                    return await _baseTaskScheduler.Enqueue(taskFunc, linkedToken.Token).ConfigureAwait(false);
                }
            }
        }
    }
}
