namespace System.Threading.Tasks.Flow
{
    /// <summary>
    /// Defines a contract for scheduling tasks for execution in a controlled manner.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="ITaskScheduler"/> interface provides a standardized way to enqueue tasks
    /// for execution according to the implementation's scheduling strategy.
    /// </para>
    /// <para>
    /// Implementations may enforce different execution patterns such as:
    /// </para>
    /// <list type="bullet">
    ///   <item>Sequential execution (one task at a time)</item>
    ///   <item>Parallel execution with controlled concurrency</item>
    ///   <item>Priority-based scheduling</item>
    ///   <item>Custom execution strategies</item>
    /// </list>
    /// </remarks>
    public interface ITaskScheduler
    {
        /// <summary>
        /// Enqueues a task function for execution according to the scheduler's strategy.
        /// </summary>
        /// <typeparam name="T">The type of result produced by the task.</typeparam>
        /// <param name="taskFunc">The function to execute that returns a <see cref="ValueTask{TResult}"/>.</param>
        /// <param name="state">An optional state object that is passed to the <paramref name="taskFunc"/>.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the result of the enqueued task.</returns>
        /// <remarks>
        /// <para>
        /// The execution behavior of this method depends on the specific implementation of the scheduler.
        /// Tasks may be executed immediately, queued for sequential execution, or handled according to
        /// other scheduling strategies.
        /// </para>
        /// <para>
        /// The returned task completes when the enqueued function completes, and will propagate any
        /// exceptions thrown by the function.
        /// </para>
        /// <para>
        /// Implementations should ensure that:
        /// </para>
        /// <list type="bullet">
        ///   <item>The provided cancellation token is honored</item>
        ///   <item>Exceptions from the task function are propagated to the returned task</item>
        ///   <item>Resources are properly managed when the scheduler is disposed</item>
        /// </list>
        /// </remarks>
        /// <exception cref="ObjectDisposedException">Thrown if the scheduler has been disposed.</exception>
        Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken);
    }
}