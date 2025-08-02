namespace System.Threading.Tasks.Flow
{
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks.Flow.Annotations;
    using System.Threading.Tasks.Flow.Internal;

    /// <summary>
    /// Base abstract class for implementing task flow execution mechanisms.
    /// Provides a foundation for creating systems that schedule and execute tasks in a controlled manner.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TaskFlowBase implements the core functionality required by the <see cref="ITaskFlow"/> interface,
    /// providing a standardized approach to task execution and lifecycle management.
    /// </para>
    /// <para>
    /// Derived classes are responsible for implementing specific task scheduling strategies
    /// through the abstract <see cref="Enqueue{T}"/> method.
    /// </para>
    /// <para>
    /// The class handles proper disposal patterns, including:
    /// - Cancellation of pending tasks during disposal
    /// - Waiting for completion of running tasks
    /// - Proper resource cleanup
    /// - Thread-safe state management
    /// </para>
    /// </remarks>
    public abstract class TaskFlowBase : ITaskFlow
    {
        private readonly TaskFlowOptions _options;
        [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Disposed in DisposeAsyncCore")]
        private readonly CancellationTokenSource _disposeCancellationTokenSource;
        private readonly object _lockObject;

        private TaskFlowState _state;

        /// <summary>
        /// Initializes a new instance of the <see cref="TaskFlowBase"/> class with the specified options.
        /// </summary>
        /// <param name="options">The options that configure the behavior of this task flow.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
        protected TaskFlowBase(TaskFlowOptions options)
        {
            Argument.NotNull(options);

            _options = options;
            _disposeCancellationTokenSource = new CancellationTokenSource();
            _lockObject = new object();
            _state = TaskFlowState.NotStarted;
        }

        /// <summary>
        /// Gets the options that configure the behavior of this task flow.
        /// </summary>
        public TaskFlowOptions Options => _options;

        /// <summary>
        /// Gets the cancellation token that is triggered when the task flow is being disposed.
        /// This token can be used to cancel pending operations during disposal.
        /// </summary>
        protected CancellationToken CompletionToken => _disposeCancellationTokenSource.Token;

        /// <summary>
        /// Gets the synchronization object that should be used for thread-safety when accessing shared state.
        /// </summary>
        protected object ThisLock => _lockObject;

        /// <summary>
        /// Asynchronously releases all resources used by this instance.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method:
        /// 1. Waits for pending initialization if the task flow was starting
        /// 2. Cancels all pending operations
        /// 3. Waits for all running operations to complete
        /// 4. Cleans up resources
        /// </para>
        /// <para>
        /// If tasks are designed to honor cancellation tokens, they will be cancelled during disposal.
        /// Otherwise, the disposal process will wait for their completion before finishing.
        /// </para>
        /// </remarks>
        /// <returns>A <see cref="ValueTask"/> representing the asynchronous disposal operation.</returns>
        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore().ConfigureAwait(false);
            Dispose(false);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Synchronously releases all resources used by this instance.
        /// </summary>
        /// <remarks>
        /// This method uses the <see cref="TaskFlowOptions.SynchronousDisposeTimeout"/> to limit
        /// the maximum wait time for task completion during disposal.
        /// </remarks>
        [SuppressMessage("Usage", "CA1816:Dispose methods should call SuppressFinalize", Justification = "GC.SuppressFinalize called in Dispose(TimeSpan)")]
        [SuppressMessage("Design", "CA1063:Implement IDisposable Correctly", Justification = "DisposeUnmanagedResources method")]
        public void Dispose()
        {
            Dispose(_options.SynchronousDisposeTimeout);
        }

        /// <summary>
        /// Synchronously releases all resources used by this instance with a specified timeout.
        /// </summary>
        /// <param name="timeout">The maximum time to wait for task completion.</param>
        /// <returns><c>true</c> if all tasks completed within the specified timeout; otherwise, <c>false</c>.</returns>
        /// <remarks>
        /// <para>
        /// This method attempts to dispose of the task flow asynchronously but with a time limit.
        /// If the specified timeout is reached before all tasks complete, the method returns <c>false</c>,
        /// but resources are still properly disposed.
        /// </para>
        /// <para>
        /// If tasks don't respond to cancellation, they may continue running after this method returns.
        /// </para>
        /// </remarks>
        [SuppressMessage("Usage", "CA1816:Dispose methods should call SuppressFinalize", Justification = "It is also Dispose")]
        public bool Dispose(TimeSpan timeout)
        {
            var result = DisposeAsync().AsTask().Await(timeout);
            Dispose(true);
            GC.SuppressFinalize(this);
            return result;
        }

        /// <summary>
        /// Enqueues a task function for execution according to the task flow's scheduling strategy.
        /// </summary>
        /// <typeparam name="T">The type of result produced by the task.</typeparam>
        /// <param name="taskFunc">The function to execute that returns a <see cref="ValueTask{TResult}"/>.</param>
        /// <param name="state">An optional state object that is passed to the <paramref name="taskFunc"/>.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the result of the enqueued task.</returns>
        /// <remarks>
        /// <para>
        /// The implementation of this method in derived classes defines the specific scheduling behavior.
        /// </para>
        /// <para>
        /// Important considerations for implementations:
        /// - The method should be thread-safe
        /// - It should check if the instance is disposed using <see cref="CheckDisposed"/> before scheduling work
        /// - Tasks should honor the provided cancellation token as well as <see cref="CompletionToken"/>
        /// </para>
        /// </remarks>
        /// <exception cref="ObjectDisposedException">Thrown if the task flow has been disposed.</exception>
        public abstract Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken);

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        /// <param name="disposing">
        /// <c>true</c> if the method is being called from <see cref="Dispose()"/> or <see cref="Dispose(TimeSpan)"/>;
        /// <c>false</c> if it's being called from a finalizer.
        /// </param>
        protected virtual void Dispose(bool disposing)
        {
        }

        /// <summary>
        /// Called during the disposal process before waiting for task completion.
        /// Derived classes can override this to perform custom actions before waiting for running tasks.
        /// </summary>
        protected virtual void OnDisposeBeforeWaitForCompletion()
        {
        }

        /// <summary>
        /// Called during the disposal process after waiting for task completion.
        /// Derived classes can override this to perform custom cleanup after all tasks have completed.
        /// </summary>
        protected virtual void OnDisposeAfterWaitForCompletion()
        {
        }

        /// <summary>
        /// Core implementation of the asynchronous disposal pattern.
        /// </summary>
        /// <returns>A <see cref="ValueTask"/> representing the asynchronous disposal operation.</returns>
        /// <remarks>
        /// This method implements a robust disposal pattern that:
        /// <list type="number">
        ///   <item>Ensures thread-safety during disposal</item>
        ///   <item>Waits for initialization to complete if the task flow was starting</item>
        ///   <item>Sets the state to disposing to prevent new tasks from being enqueued</item>
        ///   <item>Cancels all pending operations</item>
        ///   <item>Waits for all running operations to complete</item>
        ///   <item>Performs proper resource cleanup</item>
        /// </list>
        /// </remarks>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Should be observed on a tasks returned from Enqueue method")]
        protected virtual async ValueTask DisposeAsyncCore()
        {
            Task initializationTask;
            Task completionTask;
            lock (_lockObject)
            {
                initializationTask = _state != TaskFlowState.NotStarted ? GetInitializationTask() : Task.CompletedTask;

                if (_state == TaskFlowState.Disposed)
                {
                    return;
                }

                _state = TaskFlowState.Disposing;
                completionTask = GetCompletionTask();
            }

            await initializationTask.ConfigureAwait(false);

#if NET8_0_OR_GREATER
            await _disposeCancellationTokenSource.CancelAsync().ConfigureAwait(false);
#else
            _disposeCancellationTokenSource.Cancel();
#endif

            try
            {
                OnDisposeBeforeWaitForCompletion();
                await completionTask.ConfigureAwait(false);
            }
            catch
            {
                // suppressed
            }
            finally
            {
                OnDisposeAfterWaitForCompletion();
                _disposeCancellationTokenSource.Dispose();

                lock (_lockObject)
                {
                    _state = TaskFlowState.Disposed;
                }
            }
        }

        /// <summary>
        /// Sets the state of the task flow to <see cref="TaskFlowState.Running"/>.
        /// </summary>
        /// <remarks>
        /// This method should be called by derived classes when the task flow is fully initialized
        /// and ready to accept and process tasks. It transitions the task flow from the <see cref="TaskFlowState.Starting"/>
        /// or <see cref="TaskFlowState.NotStarted"/> state to the <see cref="TaskFlowState.Running"/> state.
        /// </remarks>
        protected void Ready()
        {
            lock (_lockObject)
            {
                _state = TaskFlowState.Running;
            }
        }

        /// <summary>
        /// Sets the state of the task flow to <see cref="TaskFlowState.Starting"/>.
        /// </summary>
        /// <remarks>
        /// This method should be called by derived classes when the task flow has begun its initialization
        /// process but is not yet ready to process tasks. It transitions the task flow from the
        /// <see cref="TaskFlowState.NotStarted"/> state to the <see cref="TaskFlowState.Starting"/> state.
        /// </remarks>
        protected void Starting()
        {
            lock (_lockObject)
            {
                _state = TaskFlowState.Starting;
            }
        }

        /// <summary>
        /// Checks if the task flow has been disposed and throws an <see cref="ObjectDisposedException"/> if it has.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown if the task flow is in the <see cref="TaskFlowState.Disposing"/> 
        /// or <see cref="TaskFlowState.Disposed"/> state.</exception>
        protected void CheckDisposed()
        {
#if NET7_0_OR_GREATER
            ObjectDisposedException.ThrowIf(_state is TaskFlowState.Disposing or TaskFlowState.Disposed, nameof(TaskFlow));
#else
            if (_state is TaskFlowState.Disposing or TaskFlowState.Disposed)
            {
                throw new ObjectDisposedException(nameof(TaskFlow));
            }
#endif
        }

        /// <summary>
        /// Returns a task that represents the initialization of this task flow.
        /// </summary>
        /// <returns>A <see cref="Task"/> that represents the initialization process.</returns>
        /// <remarks>
        /// This method is called during disposal to ensure that initialization completes before
        /// waiting for running tasks to complete. Derived classes should return a task that completes
        /// when the task flow is fully initialized or is ready to safely handle disposal.
        /// </remarks>
        protected abstract Task GetInitializationTask();

        /// <summary>
        /// Returns a task that represents the completion of all enqueued tasks.
        /// </summary>
        /// <returns>A <see cref="Task"/> that completes when all enqueued tasks have completed.</returns>
        /// <remarks>
        /// This method is called during disposal to wait for all currently executing and enqueued
        /// tasks to complete. Derived classes should implement this to return a task that completes
        /// only when all work items have been processed.
        /// </remarks>
        protected abstract Task GetCompletionTask();

        /// <summary>
        /// Represents the possible states of a task flow instance.
        /// </summary>
        protected enum TaskFlowState
        {
            /// <summary>
            /// The task flow has been created but not yet started.
            /// </summary>
            NotStarted = 0,

            /// <summary>
            /// The task flow has begun initialization but is not fully ready.
            /// </summary>
            Starting,

            /// <summary>
            /// The task flow is initialized and ready to accept and process tasks.
            /// </summary>
            Running,

            /// <summary>
            /// The task flow is in the process of shutting down and disposing resources.
            /// </summary>
            Disposing,

            /// <summary>
            /// The task flow has been fully disposed and can no longer be used.
            /// </summary>
            Disposed,
        }
    }
}