namespace System.Threading.Tasks.Flow
{
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using System.Runtime.ExceptionServices;
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

        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Interceptors must observe every operation and hook exception.")]
        [SuppressMessage("Performance", "CA1849:Call async methods when in an async method", Justification = "GetResult is called only for an already successfully completed ValueTask to avoid allocating an async state machine.")]
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

            private ValueTask<T> Execute<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, TaskSchedulerInterceptionContext context, CancellationToken token)
            {
                ValueTask before;
                try
                {
                    before = _interceptor.OnBeforeAsync(context);
                }
                catch (Exception exception)
                {
                    return Faulted<T>(exception);
                }

                if (before.IsCompletedSuccessfully)
                {
                    try
                    {
                        before.GetAwaiter().GetResult();
                    }
                    catch (Exception exception)
                    {
                        return Faulted<T>(exception);
                    }

                    return ExecuteOperation(taskFunc, state, context, token);
                }

                return AwaitBefore(before, taskFunc, state, context, token);
            }

            private async ValueTask<T> AwaitBefore<T>(ValueTask before, Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, TaskSchedulerInterceptionContext context, CancellationToken token)
            {
                await before.ConfigureAwait(true);
                return await ExecuteOperation(taskFunc, state, context, token).ConfigureAwait(true);
            }

            private ValueTask<T> ExecuteOperation<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, TaskSchedulerInterceptionContext context, CancellationToken token)
            {
                ValueTask<T> operation;
                try
                {
                    operation = taskFunc(state, token);
                }
                catch (Exception exception)
                {
                    return HandleError<T>(context, exception);
                }

                if (!operation.IsCompletedSuccessfully)
                {
                    return AwaitOperation(operation, context);
                }

                T result;
                try
                {
                    result = operation.GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    return HandleError<T>(context, exception);
                }

                return HandleSuccess(context, result);
            }

            private async ValueTask<T> AwaitOperation<T>(ValueTask<T> operation, TaskSchedulerInterceptionContext context)
            {
                T result;
                try
                {
                    result = await operation.ConfigureAwait(true);
                }
                catch (Exception exception)
                {
                    return await HandleError<T>(context, exception).ConfigureAwait(true);
                }

                return await HandleSuccess(context, result).ConfigureAwait(true);
            }

            private ValueTask<T> HandleSuccess<T>(TaskSchedulerInterceptionContext context, T result)
            {
                ValueTask success;
                try
                {
                    success = _interceptor.OnSuccessAsync(context, result);
                }
                catch (Exception exception)
                {
                    return Faulted<T>(exception);
                }

                if (success.IsCompletedSuccessfully)
                {
                    try
                    {
                        success.GetAwaiter().GetResult();
                        return new ValueTask<T>(result);
                    }
                    catch (Exception exception)
                    {
                        return Faulted<T>(exception);
                    }
                }

                return AwaitSuccess(success, result);
            }

            private static async ValueTask<T> AwaitSuccess<T>(ValueTask success, T result)
            {
                await success.ConfigureAwait(true);
                return result;
            }

            private ValueTask<T> HandleError<T>(TaskSchedulerInterceptionContext context, Exception exception)
            {
                ValueTask error;
                try
                {
                    error = _interceptor.OnErrorAsync(context, exception);
                }
                catch (Exception interceptorException)
                {
                    return Faulted<T>(interceptorException);
                }

                if (error.IsCompletedSuccessfully)
                {
                    try
                    {
                        error.GetAwaiter().GetResult();
                        return Faulted<T>(exception);
                    }
                    catch (Exception interceptorException)
                    {
                        return Faulted<T>(interceptorException);
                    }
                }

                return AwaitError<T>(error, exception);
            }

            private static async ValueTask<T> AwaitError<T>(ValueTask error, Exception exception)
            {
                await error.ConfigureAwait(true);
                ExceptionDispatchInfo.Capture(exception).Throw();
                return default!;
            }

            private static ValueTask<T> Faulted<T>(Exception exception) => new ValueTask<T>(Task.FromException<T>(exception));

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
