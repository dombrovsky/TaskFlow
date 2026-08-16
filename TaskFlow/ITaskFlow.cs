namespace System.Threading.Tasks.Flow
{
    /// <summary>
    /// Defines a unified interface for task flow execution that combines scheduling, information, and disposal capabilities.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="ITaskFlow"/> interface combines multiple interfaces to provide a complete API for task management:
    /// </para>
    /// <list type="bullet">
    ///   <item><see cref="ITaskScheduler"/> - For enqueueing tasks for execution</item>
    ///   <item><see cref="ITaskFlowInfo"/> - For accessing information about the task flow</item>
    ///   <item><see cref="IAsyncDisposable"/> - For asynchronous cleanup of resources</item>
    ///   <item><see cref="IDisposable"/> - For synchronous cleanup of resources</item>
    /// </list>
    /// <para>
    /// Implementations of this interface are responsible for:
    /// </para>
    /// <list type="bullet">
    ///   <item>Managing the execution of tasks in a controlled manner</item>
    ///   <item>Ensuring proper task cancellation during disposal</item>
    ///   <item>Providing configuration options through the <see cref="ITaskFlowInfo.Options"/> property</item>
    ///   <item>Supporting both synchronous and asynchronous disposal patterns</item>
    /// </list>
    /// <para>
    /// Asynchronous disposal requests cooperative cancellation and waits for tracked work. Synchronous disposal
    /// may return after its configured timeout. Tasks returned by <see cref="ITaskScheduler.Enqueue{T}"/> should
    /// always be awaited or otherwise deliberately observed because disposal does not surface their failures.
    /// </para>
    /// </remarks>
    public interface ITaskFlow : ITaskScheduler, ITaskFlowInfo, IAsyncDisposable, IDisposable
    {
        /// <summary>
        /// Synchronously disposes the task flow with a specified timeout.
        /// </summary>
        /// <param name="timeout">The maximum time to wait for task completion.</param>
        /// <returns><c>true</c> if all tasks completed within the specified timeout; otherwise, <c>false</c>.</returns>
        /// <remarks>
        /// <para>
        /// This method attempts to dispose of the task flow asynchronously but with a time limit.
        /// If the specified timeout is reached before all tasks complete, the method returns <c>false</c>.
        /// Asynchronous cleanup continues and finishes after tracked work completes.
        /// </para>
        /// <para>
        /// If tasks don't respond to cancellation, they may continue running after this method returns.
        /// </para>
        /// <para>New operations are rejected once disposal has begun.</para>
        /// </remarks>
        bool Dispose(TimeSpan timeout);
    }
}
