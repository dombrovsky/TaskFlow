namespace System.Threading.Tasks.Flow
{
    using System.Collections.Concurrent;
    using System.Diagnostics.CodeAnalysis;
    using System.Runtime.CompilerServices;
    using System.Threading.Tasks.Flow.Annotations;
    using System.Threading.Tasks.Flow.Internal;

    /// <summary>
    /// Provides a base implementation for task flows that execute tasks on a dedicated thread.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="ThreadTaskFlow"/> class serves as a base for task flow implementations that
    /// execute tasks on a specific thread. It handles the core mechanics of:
    /// </para>
    /// <list type="bullet">
    ///   <item>Enqueueing tasks for execution</item>
    ///   <item>Managing task execution on a designated thread</item>
    ///   <item>Handling synchronization context switching</item>
    ///   <item>Proper cleanup and cancellation during disposal</item>
    /// </list>
    /// <para>
    /// Derived classes must specify how the execution thread is obtained and managed by
    /// implementing the <see cref="ThreadId"/> property and initializing the thread execution.
    /// </para>
    /// </remarks>
    public abstract class ThreadTaskFlow : TaskFlowBase
    {
        [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "OnDisposeAfterWaitForCompletion")]
        private readonly BlockingCollection<ExecutionItem> _blockingCollection;
        private readonly TaskCompletionSource<Unit> _threadStartTaskSource;
        private readonly TaskCompletionSource<Unit> _threadCompletionTaskSource;
        private readonly TaskFlowSynchronizationContext _synchronizationContext;

        /// <summary>
        /// Initializes a new instance of the <see cref="ThreadTaskFlow"/> class with the specified options.
        /// </summary>
        /// <param name="options">The options that configure the behavior of this task flow.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
        /// <remarks>
        /// The constructor initializes the internal infrastructure for task execution,
        /// but derived classes are responsible for starting the execution thread
        /// and calling the <see cref="ThreadStart(object?)"/> method.
        /// </remarks>
        protected ThreadTaskFlow(TaskFlowOptions options)
            : base(options)
        {
            _blockingCollection = new BlockingCollection<ExecutionItem>();
            _threadCompletionTaskSource = new TaskCompletionSource<Unit>(TaskCreationOptions.RunContinuationsAsynchronously);
            _threadStartTaskSource = new TaskCompletionSource<Unit>(TaskCreationOptions.RunContinuationsAsynchronously);
            _synchronizationContext = new TaskFlowSynchronizationContext(this);
        }

        /// <summary>
        /// Gets the managed thread ID of the thread used for task execution.
        /// </summary>
        /// <remarks>
        /// This property should return the thread ID of the thread that is executing
        /// the tasks enqueued in this task flow. The implementation depends on how
        /// the execution thread is created or obtained in the derived class.
        /// </remarks>
        public abstract int ThreadId { get; }
        
        /// <summary>
        /// Enqueues a task function for execution on the designated thread.
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
        /// This implementation adds the task to a blocking collection that is processed by the execution thread.
        /// The task will be executed in the order it was enqueued, on the thread specified by <see cref="ThreadId"/>.
        /// </para>
        /// <para>
        /// The task function will execute with a synchronization context that ensures that any asynchronous
        /// continuations within the task function will also be marshaled back to the same thread.
        /// </para>
        /// </remarks>
        public override async Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
        {
            Argument.NotNull(taskFunc);

            lock (ThisLock)
            {
                CheckDisposed();
            }

            var executionItem = new ExecutionItem(TaskFunc, state, _synchronizationContext, cancellationToken);
            _blockingCollection.Add(executionItem, CancellationToken.None);
            return await executionItem.GetTypedTask<T>().ConfigureAwait(false);

            async Task<object?> TaskFunc(object? stateObject, CancellationToken token)
            {
                var result = await taskFunc(stateObject, token).ConfigureAwait(false);
                return result;
            }
        }

        /// <summary>
        /// Called during the disposal process before waiting for task completion.
        /// Marks the blocking collection as complete for adding, which signals the execution thread to finish.
        /// </summary>
        protected override void OnDisposeBeforeWaitForCompletion()
        {
            _blockingCollection.CompleteAdding();
        }

        /// <summary>
        /// Called during the disposal process after waiting for task completion.
        /// Disposes the blocking collection used for task execution.
        /// </summary>
        protected override void OnDisposeAfterWaitForCompletion()
        {
            _blockingCollection.Dispose();
        }

        /// <summary>
        /// Returns a task that represents the initialization of this task flow.
        /// </summary>
        /// <returns>A <see cref="Task"/> that completes when the execution thread has started.</returns>
        /// <remarks>
        /// This task completes when the execution thread has started and is ready to process tasks.
        /// </remarks>
        protected override Task GetInitializationTask()
        {
            return _threadStartTaskSource.Task;
        }

        /// <summary>
        /// Returns a task that represents the completion of all enqueued tasks.
        /// </summary>
        /// <returns>A <see cref="Task"/> that completes when the execution thread has terminated.</returns>
        /// <remarks>
        /// This task completes when the execution thread has processed all tasks and terminated.
        /// </remarks>
        protected override Task GetCompletionTask()
        {
            return _threadCompletionTaskSource.Task;
        }

        /// <summary>
        /// Starts the execution thread that processes tasks from the queue.
        /// </summary>
        /// <param name="_">An unused parameter (provided for compatibility with thread start delegates).</param>
        /// <remarks>
        /// <para>
        /// This method should be called by derived classes to start the task processing loop.
        /// It sets the task flow state to <see cref="TaskFlowState.Running"/> and begins
        /// processing tasks from the queue.
        /// </para>
        /// <para>
        /// The method will continue processing tasks until cancellation is requested or
        /// the task flow is disposed.
        /// </para>
        /// </remarks>
        protected void ThreadStart(object? _)
        {
            Ready();
            _threadStartTaskSource.SetResult(default);

            var cancellationToken = CompletionToken;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var executionItem = _blockingCollection.Take(cancellationToken);
                    executionItem.Execute(cancellationToken);
                }
            }
            catch (OperationCanceledException exception) when (exception.CancellationToken == cancellationToken)
            {
            }
            finally
            {
                var finalCancellationToken = cancellationToken.IsCancellationRequested
                    ? cancellationToken
                    : new CancellationToken(true);

                foreach (var executionItem in _blockingCollection.GetConsumingEnumerable())
                {
                    executionItem.Execute(finalCancellationToken);
                }

                _threadCompletionTaskSource.TrySetResult(default);
            }
        }

        /// <summary>
        /// Represents a task that has been enqueued for execution.
        /// </summary>
        /// <remarks>
        /// This class encapsulates the task function, state, and completion source for an enqueued task.
        /// It handles the execution of the task and propagation of results and exceptions.
        /// </remarks>
        private sealed class ExecutionItem
        {
            private readonly Func<object?, CancellationToken, Task<object?>> _taskFunc;
            private readonly object? _state;
            private readonly SynchronizationContext? _synchronizationContext;
            private readonly CancellationToken _cancellationToken;
            private readonly TaskCompletionSource<object?> _taskCompletionSource;

            /// <summary>
            /// Initializes a new instance of the <see cref="ExecutionItem"/> class.
            /// </summary>
            /// <param name="taskFunc">The function to execute.</param>
            /// <param name="state">The state object to pass to the function.</param>
            /// <param name="synchronizationContext">The synchronization context to use for continuations.</param>
            /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
            public ExecutionItem(
                Func<object?, CancellationToken, Task<object?>> taskFunc,
                object? state,
                SynchronizationContext? synchronizationContext,
                CancellationToken cancellationToken)
            {
                _taskFunc = taskFunc;
                _state = state;
                _synchronizationContext = synchronizationContext;
                _cancellationToken = cancellationToken;
                _taskCompletionSource = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            /// <summary>
            /// Gets a task that represents the completion of this execution item with the expected result type.
            /// </summary>
            /// <typeparam name="T">The expected result type.</typeparam>
            /// <returns>A task that completes with the result of the execution item.</returns>
            public async Task<T> GetTypedTask<T>()
            {
                var result = await _taskCompletionSource.Task.ConfigureAwait(false);
                return (T)result!;
            }

            /// <summary>
            /// Executes the task function and handles the result.
            /// </summary>
            /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
            [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Exception propagated to TaskCompletionSource")]
            [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "linkedCts disposed after awaiter is done")]
            public void Execute(CancellationToken cancellationToken)
            {
                var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellationToken);
                var blockingCollection = new BlockingCollection<(SendOrPostCallback Callback, object? State)>();

                try
                {
                    var synchronizationContext = new BlockingCollectionSynchronizationContext(blockingCollection, _synchronizationContext);
                    SynchronizationContext.SetSynchronizationContext(synchronizationContext);
                    var awaiter = _taskFunc(_state, linkedCts.Token).GetAwaiter();

                    if (awaiter.IsCompleted)
                    {
                        HandleAwaiterCompletion(awaiter);
                    }
                    else
                    {
                        awaiter.OnCompleted(() => HandleAwaiterCompletion(awaiter));

                        foreach (var (cb, state) in blockingCollection.GetConsumingEnumerable(CancellationToken.None))
                        {
                            SynchronizationContext.SetSynchronizationContext(synchronizationContext);
                            cb(state);
                        }
                    }
                }
                finally
                {
                    blockingCollection.Dispose();
                    linkedCts.Dispose();
                }

                return;

                void HandleAwaiterCompletion(TaskAwaiter<object?> awaiter)
                {
                    try
                    {
                        _taskCompletionSource.TrySetResult(awaiter.GetResult());
                    }
                    catch (OperationCanceledException exception)
                    {
                        _taskCompletionSource.TrySetCanceled(exception.CancellationToken);
                    }
                    catch (Exception exception)
                    {
                        _taskCompletionSource.SetException(exception);
                    }
                    finally
                    {
                        blockingCollection.CompleteAdding();
                    }
                }
            }

            /// <summary>
            /// Cancels the execution of this item.
            /// </summary>
            /// <param name="token">The cancellation token that caused the cancellation.</param>
            public void Cancel(CancellationToken token)
            {
                _taskCompletionSource.TrySetCanceled(token);
            }
        }

        /// <summary>
        /// A synchronization context that uses a blocking collection to marshal calls to a specific thread.
        /// </summary>
        private sealed class BlockingCollectionSynchronizationContext  : SynchronizationContext
        {
            private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _blockingCollection;
            private readonly SynchronizationContext? _fallbackSynchronizationContext;

            /// <summary>
            /// Initializes a new instance of the <see cref="BlockingCollectionSynchronizationContext"/> class.
            /// </summary>
            /// <param name="blockingCollection">The blocking collection to use for marshaling calls.</param>
            /// <param name="fallbackSynchronizationContext">A fallback synchronization context to use if the blocking collection is completed.</param>
            public BlockingCollectionSynchronizationContext(
                BlockingCollection<(SendOrPostCallback Callback, object? State)> blockingCollection,
                SynchronizationContext? fallbackSynchronizationContext)
            {
                _blockingCollection = blockingCollection;
                _fallbackSynchronizationContext = fallbackSynchronizationContext;
            }

            /// <summary>
            /// Dispatches an asynchronous message to the synchronization context.
            /// </summary>
            /// <param name="d">The delegate to call.</param>
            /// <param name="state">The object passed to the delegate.</param>
            public override void Post(SendOrPostCallback d, object? state)
            {
                if (_blockingCollection.IsAddingCompleted)
                {
                    if (_fallbackSynchronizationContext != null)
                    {
                        _fallbackSynchronizationContext.Post(d, state);
                    }
                    else
                    {
                        base.Post(d, state);
                    }
                }
                else
                {
                    _blockingCollection.Add((d, state));
                }
            }
        }
    }
}
