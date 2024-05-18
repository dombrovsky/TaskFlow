namespace System.Threading.Tasks.Flow
{
    using System.Linq;
    using System.Threading.Tasks.Flow.Annotations;

    public static class ExceptionTaskSchedulerExtensions
    {
        public static ITaskScheduler OnError<TException>(this ITaskScheduler taskScheduler, Action<ITaskScheduler, TException> errorAction, Func<TException, bool>? errorFilter = null)
            where TException : Exception
        {
            return new AnnotatedExceptionTaskSchedulerWrapper<TException, IOperationAnnotation>(taskScheduler, errorFilter ?? DefaultErrorFilter, (scheduler, exception, _) => errorAction(scheduler, exception));
        }

        public static ITaskScheduler OnError<TException>(this ITaskScheduler taskScheduler, Action<TException> errorAction, Func<TException, bool>? errorFilter = null)
            where TException : Exception
        {
            Argument.NotNull(errorAction);

            return new AnnotatedExceptionTaskSchedulerWrapper<TException, IOperationAnnotation>(taskScheduler, errorFilter ?? DefaultErrorFilter, (_, exception, _) => errorAction(exception));
        }

        public static ITaskScheduler OnError(this ITaskScheduler taskScheduler, Action<ITaskScheduler, Exception> errorAction)
        {
            return OnError<Exception>(taskScheduler, errorAction);
        }

        public static ITaskScheduler OnError(this ITaskScheduler taskScheduler, Action<Exception> errorAction)
        {
            return OnError<Exception>(taskScheduler, errorAction);
        }

        public static ITaskScheduler OnError<TException>(this ITaskScheduler taskScheduler, Action<ITaskScheduler, TException, OperationNameAnnotation?> errorAction, Func<TException, bool>? errorFilter = null)
            where TException : Exception
        {
            return new AnnotatedExceptionTaskSchedulerWrapper<TException, OperationNameAnnotation>(taskScheduler, errorFilter ?? DefaultErrorFilter, errorAction);
        }

        public static ITaskScheduler OnError<TException, TAnnotation>(this ITaskScheduler taskScheduler, Action<ITaskScheduler, TException, TAnnotation?> errorAction, Func<TException, bool>? errorFilter = null)
            where TException : Exception
            where TAnnotation : IOperationAnnotation
        {
            return new AnnotatedExceptionTaskSchedulerWrapper<TException, TAnnotation>(taskScheduler, errorFilter ?? DefaultErrorFilter, errorAction);
        }

        private static bool DefaultErrorFilter<TException>(TException _)
            where TException : Exception
        {
            return true;
        }

        private sealed class AnnotatedExceptionTaskSchedulerWrapper<TException, TAnnotation> : ITaskScheduler
            where TException : Exception
            where TAnnotation : IOperationAnnotation
        {
            private readonly ITaskScheduler _baseTaskScheduler;
            private readonly Func<TException, bool> _errorFilter;
            private readonly Action<ITaskScheduler, TException, TAnnotation?> _errorAction;

            public AnnotatedExceptionTaskSchedulerWrapper(
                ITaskScheduler baseTaskScheduler,
                Func<TException, bool> errorFilter,
                Action<ITaskScheduler, TException, TAnnotation?> errorAction)
            {
                Argument.NotNull(baseTaskScheduler);
                Argument.NotNull(errorFilter);
                Argument.NotNull(errorAction);

                _baseTaskScheduler = baseTaskScheduler;
                _errorFilter = errorFilter;
                _errorAction = errorAction;
            }

            public async Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
            {
                TAnnotation? annotation = default;
                if (!typeof(TAnnotation).IsInterface)
                {
                    annotation = (state as ExtendedState)
                        .Unwrap<TAnnotation>()
                        .FirstOrDefault();
                }

                try
                {
                    return await _baseTaskScheduler.Enqueue(taskFunc, state, cancellationToken).ConfigureAwait(false);
                }
                catch (TException exception) when (_errorFilter(exception))
                {
                    _errorAction(this, exception, annotation);
                    throw;
                }
            }
        }
    }
}