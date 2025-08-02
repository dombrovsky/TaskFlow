namespace System.Threading.Tasks.Flow
{
    /// <summary>
    /// A task flow implementation that executes tasks on the thread that calls the <see cref="Run"/> method.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="CurrentThreadTaskFlow"/> class provides a task scheduling mechanism that
    /// executes tasks on a specific thread provided by the caller. Unlike <see cref="DedicatedThreadTaskFlow"/>,
    /// which creates its own thread, this class allows you to use an existing thread for task execution.
    /// </para>
    /// <para>
    /// This implementation is useful when you want to execute tasks on a specific thread, such as:
    /// </para>
    /// <list type="bullet">
    ///   <item>A UI thread in a desktop application</item>
    ///   <item>A worker thread that you manage externally</item>
    ///   <item>A thread with specific properties or priority settings</item>
    /// </list>
    /// <para>
    /// Note that the <see cref="Run"/> method must be called to start the task flow, and it
    /// will block the calling thread until the task flow is disposed.
    /// </para>
    /// <example>
    /// Basic usage with a background thread:
    /// <code>
    /// var taskFlow = new CurrentThreadTaskFlow();
    /// 
    /// // Start the task flow on a background thread
    /// var thread = new Thread(() => taskFlow.Run()) { IsBackground = true };
    /// thread.Start();
    /// 
    /// // Enqueue tasks for execution on the background thread
    /// var task1 = taskFlow.Enqueue(() => Console.WriteLine("Task 1"));
    /// var task2 = taskFlow.Enqueue(() => Console.WriteLine("Task 2"));
    /// 
    /// // Wait for tasks to complete and dispose the task flow
    /// await Task.WhenAll(task1, task2);
    /// await taskFlow.DisposeAsync();
    /// </code>
    /// </example>
    /// </remarks>
    public sealed class CurrentThreadTaskFlow : ThreadTaskFlow
    {
        private int _managedThreadId;

        /// <summary>
        /// Initializes a new instance of the <see cref="CurrentThreadTaskFlow"/> class with default options.
        /// </summary>
        /// <remarks>
        /// This constructor uses the default options from <see cref="TaskFlowOptions.Default"/>.
        /// The task flow will not start processing tasks until the <see cref="Run"/> method is called.
        /// </remarks>
        public CurrentThreadTaskFlow()
            : this(TaskFlowOptions.Default)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CurrentThreadTaskFlow"/> class with the specified options.
        /// </summary>
        /// <param name="options">The options that configure the behavior of this task flow.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
        /// <remarks>
        /// The task flow will not start processing tasks until the <see cref="Run"/> method is called.
        /// </remarks>
        public CurrentThreadTaskFlow(TaskFlowOptions options)
            : base(options)
        {
        }

        /// <summary>
        /// Gets the managed thread ID of the thread that is executing tasks in this task flow.
        /// </summary>
        /// <remarks>
        /// This property returns the thread ID of the thread that called the <see cref="Run"/> method.
        /// If the <see cref="Run"/> method has not been called yet, the thread ID is not defined.
        /// </remarks>
        public override int ThreadId => _managedThreadId;

        /// <summary>
        /// Starts the task flow execution on the current thread and blocks until the task flow is disposed.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method must be called to start the task flow and begin processing tasks.
        /// It will block the calling thread until the task flow is disposed.
        /// </para>
        /// <para>
        /// Tasks enqueued before calling this method will be processed once it is called.
        /// Tasks enqueued after calling this method will be processed as they are received.
        /// </para>
        /// <para>
        /// To stop the execution, dispose the task flow from another thread or by using a cancellation token.
        /// </para>
        /// </remarks>
        public void Run()
        {
            Starting();
            _managedThreadId = Environment.CurrentManagedThreadId;
            ThreadStart(null);
        }
    }
}