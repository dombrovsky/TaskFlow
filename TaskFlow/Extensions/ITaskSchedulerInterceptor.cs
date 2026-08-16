namespace System.Threading.Tasks.Flow
{
    /// <summary>
    /// Defines synchronous callbacks that observe one operation scheduled through an <see cref="ITaskScheduler"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementations must be value types because <see cref="InterceptionTaskSchedulerExtensions.Intercept{TInterceptor}(ITaskScheduler, TInterceptor)"/>
    /// copies the configured interceptor for every operation. Mutable fields on that copy can therefore carry
    /// allocation-free, per-operation state from <see cref="OnBefore"/> to the remaining callbacks.
    /// </para>
    /// <para>
    /// Callbacks execute in the order <see cref="OnBefore"/>, the scheduled operation, then either
    /// <see cref="OnSuccess{TResult}"/> or <see cref="OnError"/>, followed by <see cref="OnFinally"/>.
    /// </para>
    /// </remarks>
    public interface ITaskSchedulerInterceptor
    {
        /// <summary>Runs immediately before the scheduled operation.</summary>
        /// <param name="context">Context describing the operation being intercepted.</param>
        /// <remarks>If this method throws, the operation is not invoked and <see cref="OnFinally"/> is still called.</remarks>
        void OnBefore(TaskSchedulerInterceptionContext context);

        /// <summary>Runs after the scheduled operation completes successfully.</summary>
        /// <typeparam name="TResult">The type of value returned by the operation.</typeparam>
        /// <param name="context">Context describing the operation being intercepted.</param>
        /// <param name="result">The value returned by the operation.</param>
        /// <remarks>An exception thrown by this method faults the returned task and is not passed to <see cref="OnError"/>.</remarks>
        void OnSuccess<TResult>(TaskSchedulerInterceptionContext context, TResult result);

        /// <summary>Runs after the scheduled operation throws or is canceled.</summary>
        /// <param name="context">Context describing the operation being intercepted.</param>
        /// <param name="exception">The exception thrown by the operation, including an <see cref="OperationCanceledException"/> for cancellation.</param>
        /// <remarks>If this method throws, its exception replaces the operation exception.</remarks>
        void OnError(TaskSchedulerInterceptionContext context, Exception exception);

        /// <summary>Runs after all other interception stages, regardless of their outcome.</summary>
        /// <param name="context">Context describing the operation being intercepted.</param>
        /// <remarks>If this method throws, its exception replaces any earlier operation or interceptor exception.</remarks>
        void OnFinally(TaskSchedulerInterceptionContext context);
    }
}
