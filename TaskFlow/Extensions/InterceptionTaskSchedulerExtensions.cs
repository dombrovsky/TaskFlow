namespace System.Threading.Tasks.Flow
{
    using System.Threading.Tasks.Flow.Annotations;

    /// <summary>Provides synchronous and asynchronous operation interception for <see cref="ITaskScheduler"/>.</summary>
    public static class InterceptionTaskSchedulerExtensions
    {
        /// <summary>Wraps a scheduler with a synchronous value-type interceptor.</summary>
        /// <typeparam name="TInterceptor">The value type that implements the lifecycle callbacks.</typeparam>
        /// <param name="taskScheduler">The scheduler whose operations will be intercepted.</param>
        /// <param name="interceptor">The interceptor template to copy for each operation.</param>
        /// <returns>A scheduler that invokes <paramref name="interceptor"/> around every operation.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <remarks>
        /// <para>
        /// A separate copy of <paramref name="interceptor"/> is made when each operation starts. Mutable fields on the
        /// copy may be used as allocation-free per-operation state and are preserved across all lifecycle callbacks.
        /// </para>
        /// <para>
        /// Interceptor callbacks run on the selected TaskFlow scheduler context. A callback exception follows the
        /// replacement semantics documented by <see cref="ITaskSchedulerInterceptor"/>.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var intercepted = scheduler.Intercept(new TimingInterceptor());
        /// var result = await intercepted.Enqueue(() => ComputeAsync());
        /// </code>
        /// </example>
        public static ITaskScheduler Intercept<TInterceptor>(this ITaskScheduler taskScheduler, TInterceptor interceptor)
            where TInterceptor : struct, ITaskSchedulerInterceptor
        {
            Argument.NotNull(taskScheduler);
            return new InterceptionTaskSchedulerWrapper<TInterceptor>(taskScheduler, interceptor);
        }

        /// <summary>Wraps a scheduler with a factory for asynchronous per-operation interceptors.</summary>
        /// <param name="taskScheduler">The scheduler whose operations will be intercepted.</param>
        /// <param name="interceptor">The factory that creates an interceptor for each operation.</param>
        /// <returns>A scheduler that asynchronously intercepts every operation.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="taskScheduler"/> or <paramref name="interceptor"/> is <c>null</c>.
        /// </exception>
        /// <remarks>
        /// <para>
        /// The factory is invoked once per operation on the selected scheduler. Every lifecycle <see cref="ValueTask"/>
        /// is awaited before execution advances, and continuations retain the TaskFlow synchronization context.
        /// </para>
        /// <para>
        /// The returned scheduler preserves the enqueue operation's cancellation and exception behavior when all
        /// interceptor callbacks complete successfully.
        /// </para>
        /// </remarks>
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
