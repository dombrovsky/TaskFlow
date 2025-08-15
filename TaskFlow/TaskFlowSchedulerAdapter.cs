namespace System.Threading.Tasks.Flow
{
    using System.Threading.Tasks.Flow.Annotations;

    /// <summary>
    /// Provides an adapter that allows .NET Framework's <see cref="TaskScheduler"/> to be used as an <see cref="ITaskScheduler"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="TaskFlowSchedulerAdapter"/> class bridges the gap between the standard .NET 
    /// <see cref="TaskScheduler"/> and the TaskFlow <see cref="ITaskScheduler"/> interface. This enables
    /// existing .NET task schedulers to be used within the TaskFlow ecosystem.
    /// </para>
    /// <para>
    /// This adapter is particularly useful for:
    /// </para>
    /// <list type="bullet">
    ///   <item>Integrating with existing code that uses standard .NET <see cref="TaskScheduler"/> implementations</item>
    ///   <item>Leveraging built-in schedulers like <see cref="TaskScheduler.Default"/> or custom schedulers</item>
    ///   <item>Migrating from standard task scheduling to the TaskFlow model incrementally</item>
    ///   <item>Using specialized schedulers (UI thread schedulers, limited concurrency schedulers, etc.)</item>
    /// </list>
    /// <para>
    /// The adapter uses <see cref="Task.Factory.StartNew(System.Func{object}, object, CancellationToken, TaskCreationOptions, TaskScheduler)"/>
    /// to schedule tasks on the underlying <see cref="TaskScheduler"/>, ensuring that all tasks are executed
    /// according to the scheduler's specific behavior and constraints.
    /// </para>
    /// </remarks>
    /// <example>
    /// <para>Using the adapter with TaskScheduler.Default:</para>
    /// <code>
    /// // Wrap the default task scheduler
    /// ITaskScheduler taskFlowScheduler = new TaskFlowSchedulerAdapter(TaskScheduler.Default);
    /// 
    /// // Now you can use it with TaskFlow extension methods
    /// var result = await taskFlowScheduler.Enqueue(async () => {
    ///     await SomeAsyncOperation();
    ///     return "completed";
    /// });
    /// </code>
    /// <para>Using with a custom task scheduler:</para>
    /// <code>
    /// // Create a limited concurrency scheduler
    /// var limitedScheduler = new LimitedConcurrencyLevelTaskScheduler(2);
    /// var adapter = new TaskFlowSchedulerAdapter(limitedScheduler);
    /// 
    /// // Tasks will be limited to 2 concurrent executions
    /// await adapter.Enqueue(() => DoWork());
    /// </code>
    /// </example>
    public sealed class TaskFlowSchedulerAdapter : ITaskScheduler
    {
        private readonly TaskScheduler _taskScheduler;

        /// <summary>
        /// Initializes a new instance of the <see cref="TaskFlowSchedulerAdapter"/> class with the specified task scheduler.
        /// </summary>
        /// <param name="taskScheduler">The .NET <see cref="TaskScheduler"/> to wrap and adapt.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <remarks>
        /// The adapter will delegate all task scheduling operations to the provided <paramref name="taskScheduler"/>,

        /// maintaining the original scheduler's execution characteristics and constraints.
        /// </remarks>
        public TaskFlowSchedulerAdapter(TaskScheduler taskScheduler)
        {
            Argument.NotNull(taskScheduler);

            _taskScheduler = taskScheduler;
        }

        /// <summary>
        /// Enqueues a task function for execution on the underlying <see cref="TaskScheduler"/>.
        /// </summary>
        /// <typeparam name="T">The type of result produced by the task function.</typeparam>
        /// <param name="taskFunc">The function to execute that accepts state and a cancellation token and returns a <see cref="ValueTask{TResult}"/>.</param>
        /// <param name="state">An optional state object that is passed to the <paramref name="taskFunc"/>.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the result of the enqueued task function.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskFunc"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the underlying task scheduler rejects the task.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the underlying task scheduler has been disposed.</exception>
        /// <remarks>
        /// <para>
        /// This method uses <see cref="Task.Factory.StartNew(System.Func{object}, object, CancellationToken, TaskCreationOptions, TaskScheduler)"/>
        /// to schedule the task function on the underlying <see cref="TaskScheduler"/>. The function is executed
        /// according to the scheduler's specific behavior and constraints.
        /// </para>
        /// <para>
        /// The method performs a double-await pattern:
        /// </para>
        /// <list type="number">
        ///   <item>First await on the task returned by <see cref="Task.Factory.StartNew(System.Func{object}, object, CancellationToken, TaskCreationOptions, TaskScheduler)"/></item>
        ///   <item>Second await on the <see cref="ValueTask{TResult}"/> returned by the task function</item>
        /// </list>
        /// <para>
        /// This ensures proper exception propagation and maintains the asynchronous nature of the task function
        /// while respecting the underlying scheduler's execution model.
        /// </para>
        /// <para>
        /// The cancellation token is honored by both the task scheduling mechanism and passed through to
        /// the task function for internal cancellation handling.
        /// </para>
        /// </remarks>
        public async Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
        {
            Argument.NotNull(taskFunc);

            var task = await Task.Factory.StartNew(() => taskFunc(state, cancellationToken), cancellationToken, TaskCreationOptions.None, _taskScheduler).ConfigureAwait(false);
            return await task.ConfigureAwait(false);
        }
    }
}