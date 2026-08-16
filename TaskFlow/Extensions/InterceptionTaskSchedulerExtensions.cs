namespace System.Threading.Tasks.Flow
{
    using System.Threading.Tasks;
    using System.Threading.Tasks.Flow.Annotations;

    /// <summary>Provides synchronous and asynchronous operation interception for <see cref="ITaskScheduler"/>.</summary>
    public static class InterceptionTaskSchedulerExtensions
    {
        /// <summary>Registers a synchronous value-type interceptor in the scheduled execution phase.</summary>
        /// <typeparam name="TInterceptor">The value type copied to create isolated state for each operation.</typeparam>
        /// <param name="taskScheduler">The scheduler or pipeline snapshot to extend.</param>
        /// <param name="interceptor">The interceptor template copied for each operation.</param>
        /// <returns>A new immutable, non-owning scheduler snapshot.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <remarks>
        /// Callbacks run on the selected terminal scheduler context in before, success/error, and finally order.
        /// Mutable fields belong to the per-operation struct copy. Callback exceptions follow the replacement
        /// semantics documented by <see cref="ITaskSchedulerInterceptor"/>.
        /// </remarks>
        public static ITaskScheduler Intercept<TInterceptor>(this ITaskScheduler taskScheduler, TInterceptor interceptor)
            where TInterceptor : struct, ITaskSchedulerInterceptor
        {
            Argument.NotNull(taskScheduler);
            return taskScheduler.UseMiddleware(new SynchronousInterceptionMiddleware<TInterceptor>(interceptor));
        }

        /// <summary>Registers a factory for asynchronous per-operation interceptors in the scheduled execution phase.</summary>
        /// <param name="taskScheduler">The scheduler or pipeline snapshot to extend.</param>
        /// <param name="interceptor">The factory that creates one asynchronous interceptor per operation.</param>
        /// <returns>A new immutable, non-owning scheduler snapshot.</returns>
        /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
        /// <remarks>
        /// The factory and every lifecycle callback run inside the terminal scheduler delegate. Returned
        /// <see cref="ValueTask"/> instances are awaited with scheduler-context capture so asynchronous callbacks,
        /// the user operation, and later completion middleware retain the selected synchronization context.
        /// </remarks>
        public static ITaskScheduler Intercept(this ITaskScheduler taskScheduler, IAsyncTaskSchedulerInterceptor interceptor)
        {
            Argument.NotNull(taskScheduler);
            Argument.NotNull(interceptor);
            return taskScheduler.UseMiddleware(new AsyncInterceptionMiddleware(interceptor));
        }

        private sealed class SynchronousInterceptionMiddleware<TInterceptor> : ITaskSchedulerExecutionMiddleware
            where TInterceptor : struct, ITaskSchedulerInterceptor
        {
            private readonly TInterceptor _interceptor;
            public SynchronousInterceptionMiddleware(TInterceptor interceptor) => _interceptor = interceptor;

            public async ValueTask<TResult> InvokeAsync<TResult>(TaskSchedulerOperationContext operationContext, TaskSchedulerExecutionDelegate<TResult> continuation)
            {
                var interceptor = _interceptor;
                var context = new TaskSchedulerInterceptionContext(operationContext);
                try
                {
                    interceptor.OnBefore(context);
                    TResult result;
                    try
                    {
                        result = await continuation(operationContext).ConfigureAwait(true);
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

        private sealed class AsyncInterceptionMiddleware : ITaskSchedulerExecutionMiddleware
        {
            private readonly IAsyncTaskSchedulerInterceptor _factory;
            public AsyncInterceptionMiddleware(IAsyncTaskSchedulerInterceptor factory) => _factory = factory;

            public async ValueTask<TResult> InvokeAsync<TResult>(TaskSchedulerOperationContext operationContext, TaskSchedulerExecutionDelegate<TResult> continuation)
            {
                var context = new TaskSchedulerInterceptionContext(operationContext);
                var interceptor = _factory.CreateInterceptor(context);
                try
                {
                    await interceptor.OnBeforeAsync(context).ConfigureAwait(true);
                    TResult result;
                    try
                    {
                        result = await continuation(operationContext).ConfigureAwait(true);
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
