namespace System.Threading.Tasks.Flow
{
    /// <summary>Synchronously observes the execution lifecycle of operations scheduled through an <see cref="ITaskScheduler"/>.</summary>
    /// <remarks>
    /// Implement this interface with a mutable value type. A separate copy of the interceptor is used for each
    /// operation, so its fields may hold allocation-free operation state between callbacks.
    /// </remarks>
    public interface ITaskSchedulerInterceptor
    {
        /// <summary>Runs immediately before the scheduled operation.</summary>
        void OnBefore(TaskSchedulerInterceptionContext context);

        /// <summary>Runs after the scheduled operation completes successfully.</summary>
        void OnSuccess<TResult>(TaskSchedulerInterceptionContext context, TResult result);

        /// <summary>Runs after the scheduled operation throws or is canceled.</summary>
        void OnError(TaskSchedulerInterceptionContext context, Exception exception);

        /// <summary>Runs after all other interception stages, regardless of their outcome.</summary>
        void OnFinally(TaskSchedulerInterceptionContext context);
    }
}
