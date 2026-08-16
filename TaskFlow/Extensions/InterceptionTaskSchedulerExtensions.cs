namespace System.Threading.Tasks.Flow
{
    using System.Threading.Tasks.Flow.Annotations;

    /// <summary>Provides asynchronous operation interception for <see cref="ITaskScheduler"/>.</summary>
    public static class InterceptionTaskSchedulerExtensions
    {
        /// <summary>Wraps a scheduler with the specified synchronous value-type interceptor.</summary>
        public static ITaskScheduler Intercept<TInterceptor>(this ITaskScheduler taskScheduler, TInterceptor interceptor)
            where TInterceptor : struct, ITaskSchedulerInterceptor
        {
            Argument.NotNull(taskScheduler);
            return new InterceptionTaskSchedulerWrapper<TInterceptor>(taskScheduler, interceptor);
        }

        /// <summary>Wraps a scheduler with the specified asynchronous interceptor.</summary>
        public static ITaskScheduler Intercept(this ITaskScheduler taskScheduler, IAsyncTaskSchedulerInterceptor interceptor)
        {
            Argument.NotNull(taskScheduler);
            Argument.NotNull(interceptor);
            return new AsyncInterceptionTaskSchedulerWrapper(taskScheduler, interceptor);
        }

        private sealed class InterceptionTaskSchedulerWrapper<TInterceptor> : ITaskScheduler
            where TInterceptor : struct, ITaskSchedulerInterceptor
        {
            private readonly ITaskScheduler _baseTaskScheduler;
            private readonly TInterceptor _interceptor;

            public InterceptionTaskSchedulerWrapper(ITaskScheduler baseTaskScheduler, TInterceptor interceptor)
            {
                _baseTaskScheduler = baseTaskScheduler;
                _interceptor = interceptor;
            }

            public Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
            {
                var context = new TaskSchedulerInterceptionContext(state, cancellationToken);
                return _baseTaskScheduler.Enqueue((_, token) => Execute(taskFunc, state, context, token), state, cancellationToken);
            }

            private async ValueTask<T> Execute<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, TaskSchedulerInterceptionContext context, CancellationToken token)
            {
                // A value-type assignment creates the independent interceptor instance used by this operation.
                var interceptor = _interceptor;
                try
                {
                    interceptor.OnBefore(context);

                    T result;
                    try
                    {
                        // Keep the TaskFlow synchronization context so subsequent interceptor callbacks run on the selected scheduler.
                        result = await taskFunc(state, token).ConfigureAwait(true);
                    }
                    catch (Exception exception)
                    {
                        interceptor.OnError(context, exception);
                        throw;
                    }

                    interceptor.OnSuccess(context, result);
                    return result;
                }
                finally
                {
                    interceptor.OnFinally(context);
                }
            }
        }

        private sealed class AsyncInterceptionTaskSchedulerWrapper : ITaskScheduler
        {
            private readonly ITaskScheduler _baseTaskScheduler;
            private readonly IAsyncTaskSchedulerInterceptor _interceptor;
            public AsyncInterceptionTaskSchedulerWrapper(ITaskScheduler baseTaskScheduler, IAsyncTaskSchedulerInterceptor interceptor)
            {
                _baseTaskScheduler = baseTaskScheduler;
                _interceptor = interceptor;
            }

            public Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
            {
                var context = new TaskSchedulerInterceptionContext(state, cancellationToken);
                return _baseTaskScheduler.Enqueue((_, token) => Execute(taskFunc, state, context, token), state, cancellationToken);
            }

            private async ValueTask<T> Execute<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, TaskSchedulerInterceptionContext context, CancellationToken token)
            {
                var interceptor = _interceptor.CreateInterceptor(context);
                try
                {
                    // ConfigureAwait(false) could move execution to the thread pool and make the operation or later callbacks leave the selected scheduler.
                    await interceptor.OnBeforeAsync(context).ConfigureAwait(true);

                    T result;
                    try
                    {
                        result = await taskFunc(state, token).ConfigureAwait(true);
                    }
                    catch (Exception exception)
                    {
                        await interceptor.OnErrorAsync(context, exception).ConfigureAwait(true);
                        throw;
                    }

                    await interceptor.OnSuccessAsync(context, result).ConfigureAwait(true);
                    return result;
                }
                finally
                {
                    await interceptor.OnFinallyAsync(context).ConfigureAwait(true);
                }
            }
        }
    }
}
