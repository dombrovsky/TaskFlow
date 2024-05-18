namespace System.Threading.Tasks.Flow
{
    using System.Linq;
    using System.Threading.Tasks.Flow.Annotations;

    public static class AnnotatingTaskSchedulerExtensions
    {
        public static ITaskScheduler WithOperationName(this ITaskScheduler taskScheduler, string operationName)
        {
            Argument.NotEmpty(operationName);

            return taskScheduler.WithExtendedState(new OperationNameAnnotation( operationName));
        }

        public static Task<T> AnnotatedEnqueue<T, TAnnotation>(this ITaskScheduler taskScheduler, Func<object?, TAnnotation?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
            where TAnnotation : class, IOperationAnnotation
        {
            Argument.NotNull(taskScheduler);
            Argument.NotNull(taskFunc);

            var annotation = (state as ExtendedState)
                .Unwrap<TAnnotation>()
                .FirstOrDefault();

            return taskScheduler.Enqueue((s, c) => taskFunc(s, annotation, c), state, cancellationToken);
        }
    }
}