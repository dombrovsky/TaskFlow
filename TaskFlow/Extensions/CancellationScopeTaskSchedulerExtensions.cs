namespace System.Threading.Tasks.Flow
{
    using System.Threading.Tasks.Flow.Annotations;

    public static class CancellationScopeTaskSchedulerExtensions
    {
        public static ITaskScheduler CreateCancellationScope(this ITaskScheduler taskScheduler, CancellationToken scopeCancellationToken)
        {
            return new CancellationScopeTaskSchedulerWrapper(taskScheduler, scopeCancellationToken);
        }

        private sealed class CancellationScopeTaskSchedulerWrapper : ITaskScheduler
        {
            private readonly ITaskScheduler _baseTaskScheduler;
            private readonly CancellationToken _scopedCancellationToken;

            public CancellationScopeTaskSchedulerWrapper(ITaskScheduler baseTaskScheduler, CancellationToken scopedCancellationToken)
            {
                Argument.NotNull(baseTaskScheduler);

                _baseTaskScheduler = baseTaskScheduler;
                _scopedCancellationToken = scopedCancellationToken;
            }

            public async Task<T> Enqueue<T>(Func<CancellationToken, ValueTask<T>> taskFunc, CancellationToken cancellationToken)
            {
                using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _scopedCancellationToken);
                return await _baseTaskScheduler.Enqueue(taskFunc, linkedToken.Token).ConfigureAwait(false);
            }
        }
    }
}