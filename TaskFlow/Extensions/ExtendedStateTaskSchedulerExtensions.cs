namespace System.Threading.Tasks.Flow;

using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks.Flow.Annotations;
using System.Threading.Tasks.Flow.Internal;

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

        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Disposed calls throw synchronously; other scheduling errors retain task-based propagation.")]
        public Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
        {
            var extendedState = new ExtendedState(state, _extendedState);

            try
            {
                return _baseTaskScheduler.Enqueue(Action, extendedState, cancellationToken);
            }
            catch (Exception exception) when (exception is not ObjectDisposedException)
            {
                return TaskExtensions.FromAsyncException<T>(exception);
            }

            async ValueTask<T> Action(ExtendedState s, CancellationToken token)
            {
                return await taskFunc(s.State, token).ConfigureAwait(false);
            }
        }
    }
}