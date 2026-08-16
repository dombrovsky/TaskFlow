namespace System.Threading.Tasks.Flow
{
    /// <summary>Creates an asynchronous interceptor for each operation scheduled through an <see cref="ITaskScheduler"/>.</summary>
    public interface IAsyncTaskSchedulerInterceptor
    {
        /// <summary>Creates the interceptor that observes a single scheduled operation.</summary>
        IAsyncTaskInterceptor CreateInterceptor(TaskSchedulerInterceptionContext context);
    }
}
