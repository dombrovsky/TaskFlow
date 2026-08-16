namespace System.Threading.Tasks.Flow
{
    /// <summary>Asynchronously observes the execution lifecycle of one scheduled operation.</summary>
    public interface IAsyncTaskInterceptor
    {
        /// <summary>Runs immediately before the scheduled operation.</summary>
        ValueTask OnBeforeAsync(TaskSchedulerInterceptionContext context);

        /// <summary>Runs after the scheduled operation completes successfully.</summary>
        ValueTask OnSuccessAsync<TResult>(TaskSchedulerInterceptionContext context, TResult result);

        /// <summary>Runs after the scheduled operation throws or is canceled.</summary>
        ValueTask OnErrorAsync(TaskSchedulerInterceptionContext context, Exception exception);

        /// <summary>Runs after all other interception stages, regardless of their outcome.</summary>
        ValueTask OnFinallyAsync(TaskSchedulerInterceptionContext context);
    }
}
