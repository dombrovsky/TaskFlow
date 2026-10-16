namespace System.Threading.Tasks.Flow
{
    /// <summary>Creates one middleware instance owned by a registration's lifetime.</summary>
    public interface ITaskSchedulerMiddlewareFactory
    {
        /// <summary>Creates middleware. Cleanup must not be required to settle active submissions.</summary>
        ITaskSchedulerMiddleware Create(TaskSchedulerMiddlewareLifetime lifetime);
    }
}
