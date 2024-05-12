namespace System.Threading.Tasks.Flow
{
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks.Flow.Annotations;
    using System.Threading.Tasks.Flow.Internal;

    public abstract class TaskFlowBase : ITaskFlow
    {
        private readonly TaskFlowOptions _options;
        [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Disposed in DisposeAsyncCore")]
        private readonly CancellationTokenSource _disposeCancellationTokenSource;
        private readonly object _lockObject;

        private TaskFlowState _state;

        protected TaskFlowBase(TaskFlowOptions options)
        {
            Argument.NotNull(options);

            _options = options;
            _disposeCancellationTokenSource = new CancellationTokenSource();
            _lockObject = new object();
            _state = TaskFlowState.NotStarted;
        }

        public TaskFlowOptions Options => _options;

        protected CancellationToken CompletionToken => _disposeCancellationTokenSource.Token;

        protected object ThisLock => _lockObject;

        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore().ConfigureAwait(false);
            Dispose(false);
            GC.SuppressFinalize(this);
        }

        [SuppressMessage("Usage", "CA1816:Dispose methods should call SuppressFinalize", Justification = "GC.SuppressFinalize called in Dispose(TimeSpan)")]
        [SuppressMessage("Design", "CA1063:Implement IDisposable Correctly", Justification = "DisposeUnmanagedResources method")]
        public void Dispose()
        {
            Dispose(_options.SynchronousDisposeTimeout);
        }

        [SuppressMessage("Usage", "CA1816:Dispose methods should call SuppressFinalize", Justification = "It is also Dispose")]
        public bool Dispose(TimeSpan timeout)
        {
            var result = DisposeAsync().AsTask().Await(timeout);
            Dispose(true);
            GC.SuppressFinalize(this);
            return result;
        }

        public abstract Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken);

        protected virtual void Dispose(bool disposing)
        {
        }

        protected virtual void OnDisposeBeforeWaitForCompletion()
        {
        }

        protected virtual void OnDisposeAfterWaitForCompletion()
        {
        }

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

        protected void Ready()
        {
            lock (_lockObject)
            {
                _state = TaskFlowState.Running;
            }
        }

        protected void Starting()
        {
            lock (_lockObject)
            {
                _state = TaskFlowState.Starting;
            }
        }

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

        protected abstract Task GetInitializationTask();

        protected abstract Task GetCompletionTask();

        protected enum TaskFlowState
        {
            NotStarted = 0,

            Starting,

            Running,

            Disposing,

            Disposed,
        }
    }
}