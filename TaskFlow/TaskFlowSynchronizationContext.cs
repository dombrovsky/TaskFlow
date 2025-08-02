namespace System.Threading.Tasks.Flow
{
    using System.Threading.Tasks.Flow.Annotations;
    using System.Threading.Tasks.Flow.Internal;

    /// <summary>
    /// Provides a <see cref="SynchronizationContext"/> implementation that executes callbacks using a <see cref="ITaskScheduler"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="TaskFlowSynchronizationContext"/> class enables integration between the .NET synchronization context
    /// model and the TaskFlow task scheduling system. This allows code that expects to run on a specific
    /// synchronization context to execute using a task scheduler.
    /// </para>
    /// <para>
    /// This is particularly useful for:
    /// </para>
    /// <list type="bullet">
    ///   <item>Supporting async/await operations that need to return to a specific context</item>
    ///   <item>Enabling TaskFlow to be used with code that relies on synchronization contexts</item>
    ///   <item>Creating a sequential execution environment for asynchronous operations</item>
    /// </list>
    /// <example>
    /// Using TaskFlowSynchronizationContext to execute code on a TaskFlow:
    /// <code>
    /// using var taskFlow = new TaskFlow();
    /// var originalContext = SynchronizationContext.Current;
    /// 
    /// try 
    /// {
    ///     // Set the TaskFlow as the current synchronization context
    ///     SynchronizationContext.SetSynchronizationContext(
    ///         TaskFlowSynchronizationContext.For(taskFlow));
    ///     
    ///     // Async operations will now return to the TaskFlow context
    ///     await DoSomethingAsync();
    ///     
    ///     // Code here runs on the TaskFlow
    /// }
    /// finally
    /// {
    ///     // Restore the original context
    ///     SynchronizationContext.SetSynchronizationContext(originalContext);
    /// }
    /// </code>
    /// </example>
    /// </remarks>
    public sealed class TaskFlowSynchronizationContext : SynchronizationContext
    {
        private readonly ITaskScheduler _taskScheduler;

        /// <summary>
        /// Initializes a new instance of the <see cref="TaskFlowSynchronizationContext"/> class with the specified task scheduler.
        /// </summary>
        /// <param name="taskScheduler">The task scheduler to use for executing callbacks.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is null.</exception>
        public TaskFlowSynchronizationContext(ITaskScheduler taskScheduler)
        {
            Argument.NotNull(taskScheduler);

            _taskScheduler = taskScheduler;
        }

        /// <summary>
        /// Creates a new <see cref="SynchronizationContext"/> that executes callbacks using the specified task scheduler.
        /// </summary>
        /// <param name="taskScheduler">The task scheduler to use for executing callbacks.</param>
        /// <returns>A <see cref="SynchronizationContext"/> that uses the specified task scheduler.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is null.</exception>
        /// <remarks>
        /// This is a convenience factory method for creating a new <see cref="TaskFlowSynchronizationContext"/>
        /// instance that returns it as a base <see cref="SynchronizationContext"/> type.
        /// </remarks>
        public static SynchronizationContext For(ITaskScheduler taskScheduler)
        {
            return new TaskFlowSynchronizationContext(taskScheduler);
        }

        /// <summary>
        /// Synchronously executes the specified delegate on the task scheduler.
        /// </summary>
        /// <param name="d">The delegate to execute.</param>
        /// <param name="state">The state object to pass to the delegate.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="d"/> is null.</exception>
        /// <remarks>
        /// <para>
        /// This method blocks the calling thread until the delegate completes execution.
        /// </para>
        /// <para>
        /// The delegate is executed using the task scheduler associated with this context,
        /// which ensures proper integration with the TaskFlow execution model.
        /// </para>
        /// </remarks>
        public override void Send(SendOrPostCallback d, object? state)
        {
            Argument.NotNull(d);

            _taskScheduler.Enqueue(() => d(state)).Await();
        }

        /// <summary>
        /// Asynchronously executes the specified delegate on the task scheduler.
        /// </summary>
        /// <param name="d">The delegate to execute.</param>
        /// <param name="state">The state object to pass to the delegate.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="d"/> is null.</exception>
        /// <remarks>
        /// <para>
        /// This method does not block the calling thread and returns immediately.
        /// </para>
        /// <para>
        /// The delegate is queued for execution using the task scheduler associated with this context,
        /// which ensures proper integration with the TaskFlow execution model.
        /// </para>
        /// </remarks>
        public override void Post(SendOrPostCallback d, object? state)
        {
            Argument.NotNull(d);

            _ = _taskScheduler.Enqueue(() => d(state));
        }
    }
}