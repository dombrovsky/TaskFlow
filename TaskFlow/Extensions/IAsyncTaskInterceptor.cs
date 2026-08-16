namespace System.Threading.Tasks.Flow
{
    /// <summary>Defines asynchronous callbacks that observe one scheduled operation.</summary>
    /// <remarks>
    /// Each returned <see cref="ValueTask"/> is awaited exactly once before the lifecycle advances. Callbacks execute
    /// in the order <see cref="OnBeforeAsync"/>, the operation, then either <see cref="OnSuccessAsync{TResult}"/> or
    /// <see cref="OnErrorAsync"/>, followed by <see cref="OnFinallyAsync"/>.
    /// </remarks>
    public interface IAsyncTaskInterceptor
    {
        /// <summary>Runs immediately before the scheduled operation.</summary>
        /// <param name="context">Context describing the operation being intercepted.</param>
        /// <returns>A value task that completes when the callback has finished.</returns>
        /// <remarks>If this method fails, the operation is not invoked and <see cref="OnFinallyAsync"/> is still called.</remarks>
        ValueTask OnBeforeAsync(TaskSchedulerInterceptionContext context);

        /// <summary>Runs after the scheduled operation completes successfully.</summary>
        /// <typeparam name="TResult">The type of value returned by the operation.</typeparam>
        /// <param name="context">Context describing the operation being intercepted.</param>
        /// <param name="result">The value returned by the operation.</param>
        /// <returns>A value task that completes when the callback has finished.</returns>
        /// <remarks>A failure from this method faults the returned task and is not passed to <see cref="OnErrorAsync"/>.</remarks>
        ValueTask OnSuccessAsync<TResult>(TaskSchedulerInterceptionContext context, TResult result);

        /// <summary>Runs after the scheduled operation throws or is canceled.</summary>
        /// <param name="context">Context describing the operation being intercepted.</param>
        /// <param name="exception">The exception thrown by the operation, including an <see cref="OperationCanceledException"/> for cancellation.</param>
        /// <returns>A value task that completes when the callback has finished.</returns>
        /// <remarks>If this method fails, its exception replaces the operation exception.</remarks>
        ValueTask OnErrorAsync(TaskSchedulerInterceptionContext context, Exception exception);

        /// <summary>Runs after all other interception stages, regardless of their outcome.</summary>
        /// <param name="context">Context describing the operation being intercepted.</param>
        /// <returns>A value task that completes when the callback has finished.</returns>
        /// <remarks>If this method fails, its exception replaces any earlier operation or interceptor exception.</remarks>
        ValueTask OnFinallyAsync(TaskSchedulerInterceptionContext context);
    }
}
