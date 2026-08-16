namespace System.Threading.Tasks.Flow
{
    /// <summary>
    /// Creates an asynchronous interceptor for each operation scheduled through an <see cref="ITaskScheduler"/>.
    /// </summary>
    /// <remarks>
    /// The factory may be shared by concurrent operations. It must return an independent interceptor whenever
    /// lifecycle state is stored in interceptor fields. Unlike synchronous struct interception, creating an
    /// asynchronous per-operation interceptor may allocate.
    /// </remarks>
    public interface IAsyncTaskSchedulerInterceptor
    {
        /// <summary>Creates the interceptor that observes a single scheduled operation.</summary>
        /// <param name="context">Context describing the operation that will be intercepted.</param>
        /// <returns>A non-<c>null</c> interceptor dedicated to the operation.</returns>
        /// <remarks>
        /// This method runs on the selected scheduler immediately before lifecycle interception begins. If it throws,
        /// the operation is not invoked and no lifecycle callback can be made for that operation.
        /// </remarks>
        IAsyncTaskInterceptor CreateInterceptor(TaskSchedulerInterceptionContext context);
    }
}
