namespace System.Threading.Tasks.Flow
{
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks.Flow.Annotations;

    /// <summary>
    /// Represents a task flow implementation that executes tasks sequentially in order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="TaskFlow"/> class provides a task scheduling mechanism that guarantees
    /// sequential execution of tasks in the order they are enqueued. Each task will only start
    /// after the previous task has completed.
    /// </para>
    /// <para>
    /// This implementation is useful when you need to ensure that tasks are executed in a strict
    /// sequential order without concurrency.
    /// </para>
    /// <para>
    /// Tasks are executed on the thread pool or using the custom <see cref="TaskScheduler"/>
    /// specified in the <see cref="TaskFlowOptions"/>.
    /// </para>
    /// <example>
    /// Basic usage:
    /// <code>
    /// using var taskFlow = new TaskFlow();
    /// 
    /// // Enqueue multiple tasks
    /// var task1 = taskFlow.Enqueue(() => Console.WriteLine("Task 1"));
    /// var task2 = taskFlow.Enqueue(() => Console.WriteLine("Task 2"));
    /// var task3 = taskFlow.Enqueue(() => Console.WriteLine("Task 3"));
    /// 
    /// // Wait for all tasks to complete
    /// await Task.WhenAll(task1, task2, task3);
    /// </code>
    /// </example>
    /// </remarks>
    public sealed partial class TaskFlow : TaskFlowBase
    {
        private Task _task;

        /// <summary>
        /// Initializes a new instance of the <see cref="TaskFlow"/> class with default options.
        /// </summary>
        /// <remarks>
        /// This constructor uses the default options from <see cref="TaskFlowOptions.Default"/>.
        /// </remarks>
        public TaskFlow()
         : this(TaskFlowOptions.Default)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TaskFlow"/> class with the specified options.
        /// </summary>
        /// <param name="options">The options that configure the behavior of this task flow.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
        public TaskFlow(TaskFlowOptions options)
            : base(options)
        {
            _task = Task.CompletedTask;
            Ready();
        }

        /// <summary>
        /// Enqueues a task function for sequential execution after all previously enqueued tasks have completed.
        /// </summary>
        /// <typeparam name="T">The type of result produced by the task.</typeparam>
        /// <param name="taskFunc">The function to execute that returns a <see cref="ValueTask{TResult}"/>.</param>
        /// <param name="state">An optional state object that is passed to the <paramref name="taskFunc"/>.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the result of the enqueued task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskFunc"/> is null.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the task flow has been disposed.</exception>
        /// <remarks>
        /// <para>
        /// This implementation ensures that tasks are executed sequentially in the order they are enqueued.
        /// Each task will only start after the previous task has completed.
        /// </para>
        /// <para>
        /// The task is scheduled using the <see cref="TaskScheduler"/> specified in the <see cref="TaskFlowOptions"/>.
        /// </para>
        /// <para>
        /// Cancellation can occur in two ways:
        /// </para>
        /// <list type="bullet">
        ///   <item>Through the provided <paramref name="cancellationToken"/></item>
        ///   <item>Through the disposal of the <see cref="TaskFlow"/> instance</item>
        /// </list>
        /// <para>
        /// If a previous task fails with an exception, the exception is suppressed for the continuity
        /// of the task flow, but the task's exception can still be observed by awaiting the task returned
        /// from the original <see cref="Enqueue{T}"/> call.
        /// </para>
        /// </remarks>
        public override Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
        {
            Argument.NotNull(taskFunc);

            lock (ThisLock)
            {
                CheckDisposed();

                var previousTask = _task;
                var task = Task.Factory.StartNew(
                    () => RunAfterPrevious(taskFunc, state, previousTask, cancellationToken),
                    CancellationToken.None,
                    TaskCreationOptions.PreferFairness,
                    Options.TaskScheduler).Unwrap();
                _task = task.ContinueWith(EmptyContinuationAction, CancellationToken.None, TaskContinuationOptions.None, Options.TaskScheduler);
                return task;
            }

            static void EmptyContinuationAction(Task obj)
            {
            }
        }

        /// <summary>
        /// Returns a task that represents the initialization of this task flow.
        /// </summary>
        /// <returns>A <see cref="Task"/> that represents the initialization process.</returns>
        /// <remarks>
        /// This implementation always returns a completed task since the <see cref="TaskFlow"/>
        /// is immediately ready upon construction.
        /// </remarks>
        protected override Task GetInitializationTask()
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Returns a task that represents the completion of all enqueued tasks.
        /// </summary>
        /// <returns>A <see cref="Task"/> that completes when all enqueued tasks have completed.</returns>
        /// <remarks>
        /// This implementation returns the most recently scheduled task, which represents
        /// the completion of all previous tasks due to the sequential execution model.
        /// </remarks>
        protected override Task GetCompletionTask()
        {
            lock (ThisLock)
            {
                return _task;
            }
        }

        /// <summary>
        /// Executes the task function after the previous task has completed.
        /// </summary>
        /// <typeparam name="T">The type of result produced by the task.</typeparam>
        /// <param name="taskFunc">The function to execute that returns a <see cref="ValueTask{TResult}"/>.</param>
        /// <param name="state">An optional state object that is passed to the <paramref name="taskFunc"/>.</param>
        /// <param name="previousTask">The previous task that must complete before this task can start.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the result of the task.</returns>
        /// <remarks>
        /// This method ensures sequential execution by awaiting the completion of the previous task
        /// before executing the current task function. Exceptions from the previous task are caught
        /// and suppressed to ensure the continuity of the task flow.
        /// </remarks>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Previous task exception should be observed on a task returned from Enqueue method")]
        private async Task<T> RunAfterPrevious<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, Task previousTask, CancellationToken cancellationToken)
        {
            try
            {
                await previousTask.ConfigureAwait(false);
            }
            catch
            {
                // suppressed
            }

            using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, CompletionToken);
            return await taskFunc(state, linkedToken.Token).ConfigureAwait(false);
        }
    }
}