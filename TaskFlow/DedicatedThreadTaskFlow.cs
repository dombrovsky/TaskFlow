namespace System.Threading.Tasks.Flow
{
    /// <summary>
    /// A task flow implementation that executes tasks on a dedicated background thread.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="DedicatedThreadTaskFlow"/> class provides a task scheduling mechanism that
    /// executes tasks on a dedicated background thread created specifically for this task flow.
    /// The thread is automatically started during construction and continues running until the task flow is disposed.
    /// </para>
    /// <para>
    /// This implementation is useful when you want to:
    /// </para>
    /// <list type="bullet">
    ///   <item>Execute tasks in a background thread that doesn't block the main application thread</item>
    ///   <item>Process tasks sequentially in a dedicated thread</item>
    ///   <item>Isolate task execution from other parts of the application</item>
    /// </list>
    /// <para>
    /// The dedicated thread is created as a background thread, which means it will not prevent
    /// the application from exiting if all foreground threads have terminated.
    /// </para>
    /// <example>
    /// Basic usage:
    /// <code>
    /// // Create a task flow with a dedicated thread named "MyBackgroundThread"
    /// using var taskFlow = new DedicatedThreadTaskFlow("MyBackgroundThread");
    /// 
    /// // Enqueue tasks for execution on the dedicated thread
    /// var task1 = taskFlow.Enqueue(() => Console.WriteLine("Task 1"));
    /// var task2 = taskFlow.Enqueue(() => Console.WriteLine("Task 2"));
    /// 
    /// // Wait for tasks to complete
    /// await Task.WhenAll(task1, task2);
    /// </code>
    /// </example>
    /// </remarks>
    public sealed class DedicatedThreadTaskFlow : ThreadTaskFlow
    {
        private readonly Thread _thread;

        /// <summary>
        /// Initializes a new instance of the <see cref="DedicatedThreadTaskFlow"/> class with default options and an optional name.
        /// </summary>
        /// <param name="name">Optional name for the dedicated thread. If null or empty, uses the class name.</param>
        /// <remarks>
        /// <para>
        /// This constructor uses the default options from <see cref="TaskFlowOptions.Default"/>.
        /// </para>
        /// <para>
        /// The task flow immediately creates and starts a dedicated background thread for processing tasks.
        /// </para>
        /// </remarks>
        public DedicatedThreadTaskFlow(string? name = default)
            : this(TaskFlowOptions.Default, name)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DedicatedThreadTaskFlow"/> class with the specified options and an optional name.
        /// </summary>
        /// <param name="options">The options that configure the behavior of this task flow.</param>
        /// <param name="name">Optional name for the dedicated thread. If null or empty, uses the class name.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
        /// <remarks>
        /// <para>
        /// The task flow immediately creates and starts a dedicated background thread for processing tasks.
        /// </para>
        /// <para>
        /// The thread name is useful for debugging and thread identification in thread dumps or profiling tools.
        /// </para>
        /// </remarks>
        public DedicatedThreadTaskFlow(TaskFlowOptions options, string? name = default)
            : base(options)
        {
            _thread = new Thread(ThreadStart)
            {
                Name = string.IsNullOrEmpty(name) ? nameof(DedicatedThreadTaskFlow) : name,
                IsBackground = true,
            };

            Starting();
            _thread.Start(null);
        }

        /// <summary>
        /// Gets the managed thread ID of the dedicated thread that is executing tasks in this task flow.
        /// </summary>
        /// <remarks>
        /// This property returns the thread ID of the dedicated thread created for this task flow.
        /// </remarks>
        public override int ThreadId => _thread.ManagedThreadId;
    }
}