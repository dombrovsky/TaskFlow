namespace System.Threading.Tasks.Flow
{
    using System.Linq;
    using System.Threading.Tasks.Flow.Annotations;

    /// <summary>Provides asynchronous operation interception for <see cref="ITaskScheduler"/>.</summary>
    public static class InterceptionTaskSchedulerExtensions
    {
        /// <summary>Wraps a scheduler with the specified asynchronous interceptor.</summary>
        public static ITaskScheduler Intercept(this ITaskScheduler taskScheduler, ITaskSchedulerInterceptor interceptor)
        {
            Argument.NotNull(taskScheduler);
            Argument.NotNull(interceptor);
            return new InterceptionTaskSchedulerWrapper(taskScheduler, interceptor);
        }

        private sealed class InterceptionTaskSchedulerWrapper : ITaskScheduler
        {
            private readonly ITaskScheduler _baseTaskScheduler;
            private readonly ITaskSchedulerInterceptor _interceptor;
            private long _lastOperationId;

            public InterceptionTaskSchedulerWrapper(ITaskScheduler baseTaskScheduler, ITaskSchedulerInterceptor interceptor)
            {
                _baseTaskScheduler = baseTaskScheduler;
                _interceptor = interceptor;
            }

            public Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
            {
                var operationId = Interlocked.Increment(ref _lastOperationId);
                var operationName = (state as ExtendedState).Unwrap<OperationNameAnnotation>().FirstOrDefault()?.OperationName;
                var context = new TaskSchedulerInterceptionContext(operationId, operationName, UnwrapState(state), cancellationToken);
                return _baseTaskScheduler.Enqueue((_, token) => Execute(taskFunc, state, context, token), state, cancellationToken);
            }

            private async ValueTask<T> Execute<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, TaskSchedulerInterceptionContext context, CancellationToken token)
            {
                try
                {
                    await _interceptor.OnBeforeAsync(context).ConfigureAwait(true);

                    T result;
                    try
                    {
                        result = await taskFunc(state, token).ConfigureAwait(true);
                    }
                    catch (Exception exception)
                    {
                        await _interceptor.OnErrorAsync(context, exception).ConfigureAwait(true);
                        throw;
                    }

                    await _interceptor.OnSuccessAsync(context, result).ConfigureAwait(true);
                    return result;
                }
                finally
                {
                    await _interceptor.OnFinallyAsync(context).ConfigureAwait(true);
                }
            }

            private static object? UnwrapState(object? state)
            {
                while (state is ExtendedState extendedState)
                {
                    state = extendedState.State;
                }

                return state;
            }
        }
    }
}
