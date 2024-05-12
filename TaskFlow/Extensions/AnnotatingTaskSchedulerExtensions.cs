namespace System.Threading.Tasks.Flow
{
    using System.Threading.Tasks.Flow.Annotations;

    public static class AnnotatingTaskSchedulerExtensions
    {
        public static ITaskScheduler WithOperationName(this ITaskScheduler taskScheduler, string operationName)
        {
            Argument.NotEmpty(operationName);

            return taskScheduler.WithExtendedState(new OperationAnnotation { OperationName = operationName });
        }
    }
}