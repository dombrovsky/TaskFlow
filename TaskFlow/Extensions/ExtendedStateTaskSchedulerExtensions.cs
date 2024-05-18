namespace System.Threading.Tasks.Flow
{
    using System.Threading.Tasks.Flow.Annotations;

    internal static class ExtendedStateTaskSchedulerExtensions
    {
        public static ITaskScheduler WithExtendedState<TState>(this ITaskScheduler taskScheduler, TState extendedState)
        {
            return new ExtendedStateTaskSchedulerWrapper<TState>(taskScheduler, extendedState);
        }

        private sealed class ExtendedStateTaskSchedulerWrapper<TState> : ITaskScheduler
        {
            private readonly ITaskScheduler _baseTaskScheduler;
            private readonly TState _extendedState;

            public ExtendedStateTaskSchedulerWrapper(ITaskScheduler baseTaskScheduler, TState extendedState)
            {
                Argument.NotNull(baseTaskScheduler);

                _baseTaskScheduler = baseTaskScheduler;
                _extendedState = extendedState;
            }

            public async Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
            {
                var extendedState = new ExtendedState(state, _extendedState);

                return await _baseTaskScheduler.Enqueue(Action, extendedState, cancellationToken).ConfigureAwait(false);

                async ValueTask<T> Action(ExtendedState s, CancellationToken token)
                {
                    return await taskFunc(s.State, token).ConfigureAwait(false);
                }
            }
        }
    }
}