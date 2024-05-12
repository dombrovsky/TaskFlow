namespace System.Threading.Tasks.Flow
{
    using System.Collections.Concurrent;
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks.Flow.Annotations;
    using System.Threading.Tasks.Flow.Internal;

    public abstract class ThreadTaskFlow : TaskFlowBase
    {
        [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "OnDisposeAfterWaitForCompletion")]
        private readonly BlockingCollection<ExecutionItem> _blockingCollection;
        private readonly TaskCompletionSource<Unit> _threadStartTaskSource;
        private readonly TaskCompletionSource<Unit> _threadCompletionTaskSource;
        private readonly TaskFlowSynchronizationContext _synchronizationContext;

        protected ThreadTaskFlow(TaskFlowOptions options)
            : base(options)
        {
            _blockingCollection = new BlockingCollection<ExecutionItem>();
            _threadCompletionTaskSource = new TaskCompletionSource<Unit>(TaskCreationOptions.RunContinuationsAsynchronously);
            _threadStartTaskSource = new TaskCompletionSource<Unit>(TaskCreationOptions.RunContinuationsAsynchronously);
            _synchronizationContext = new TaskFlowSynchronizationContext(this);
        }

        public abstract int ThreadId { get; }
        
        public override async Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
        {
            Argument.NotNull(taskFunc);

            lock (ThisLock)
            {
                CheckDisposed();
            }

            var executionItem = new ExecutionItem(TaskFunc, state, cancellationToken);
            _blockingCollection.Add(executionItem, CancellationToken.None);
            return await executionItem.GetTypedTask<T>().ConfigureAwait(false);

            async Task<object?> TaskFunc(object? stateObject, CancellationToken token)
            {
                var result = await taskFunc(stateObject, token).ConfigureAwait(false);
                return result;
            }
        }

        protected override void OnDisposeBeforeWaitForCompletion()
        {
            _blockingCollection.CompleteAdding();
        }

        protected override void OnDisposeAfterWaitForCompletion()
        {
            _blockingCollection.Dispose();
        }

        protected override Task GetInitializationTask()
        {
            return _threadStartTaskSource.Task;
        }

        protected override Task GetCompletionTask()
        {
            return _threadCompletionTaskSource.Task;
        }

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
                    SynchronizationContext.SetSynchronizationContext(_synchronizationContext);
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
                    SynchronizationContext.SetSynchronizationContext(_synchronizationContext);
                    executionItem.Execute(finalCancellationToken);
                }

                _threadCompletionTaskSource.TrySetResult(default);
            }
        }

        private sealed class ExecutionItem
        {
            private readonly Func<object?, CancellationToken, Task<object?>> _taskFunc;
            private readonly object? _state;
            private readonly CancellationToken _cancellationToken;
            private readonly TaskCompletionSource<object?> _taskCompletionSource;

            public ExecutionItem(Func<object?, CancellationToken, Task<object?>> taskFunc, object? state, CancellationToken cancellationToken)
            {
                _taskFunc = taskFunc;
                _state = state;
                _cancellationToken = cancellationToken;
                _taskCompletionSource = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public async Task<T> GetTypedTask<T>()
            {
                var result = await _taskCompletionSource.Task.ConfigureAwait(false);
                return (T)result!;
            }

            [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Exception propagated to TaskCompletionSource")]
            public void Execute(CancellationToken cancellationToken)
            {
                var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellationToken);
                try
                {
                    var result = _taskFunc(_state, linkedCts.Token).GetAwaiter().GetResult();
                    _taskCompletionSource.TrySetResult(result);
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
                    linkedCts.Dispose();
                }
            }

            public void Cancel(CancellationToken token)
            {
                _taskCompletionSource.TrySetCanceled(token);
            }
        }
    }
}
