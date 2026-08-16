namespace System.Threading.Tasks.Flow
{
    /// <summary>Observes the execution lifecycle of operations scheduled through an <see cref="ITaskScheduler"/>.</summary>
    public interface ITaskSchedulerInterceptor
    {
        /// <summary>Runs immediately before the scheduled operation.</summary>
        ValueTask OnBeforeAsync(TaskSchedulerInterceptionContext context);

        /// <summary>Runs after the scheduled operation completes successfully.</summary>
        ValueTask OnSuccessAsync<TResult>(TaskSchedulerInterceptionContext context, TResult result);

        /// <summary>Runs after the scheduled operation throws or is canceled.</summary>
        ValueTask OnErrorAsync(TaskSchedulerInterceptionContext context, Exception exception);
    }
}
