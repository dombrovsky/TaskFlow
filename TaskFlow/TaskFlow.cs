namespace System.Threading.Tasks.Flow
{
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks.Flow.Annotations;

    public sealed partial class TaskFlow : ITaskFlow
    {
        private readonly TaskScheduler _taskScheduler;
        private readonly CancellationTokenSource _disposeCancellationTokenSource;
        private readonly object _lockObject;

        private Task _task;
        private State _state;

        public TaskFlow()
         : this(TaskScheduler)
        {
        }

        public TaskFlow(TaskScheduler taskScheduler)
        {
            _taskScheduler = taskScheduler;
            _disposeCancellationTokenSource = new CancellationTokenSource();
            _lockObject = new object();
            _task = Task.CompletedTask;
            _state = State.Running;
        }

        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Previous task exception should be observed on a task returned from Enqueue method")]
        public async ValueTask DisposeAsync()
        {
            Task task;
            lock (_lockObject)
            {
                if (_state == State.Disposed)
                {
                    return;
                }

                _state = State.Disposing;
                task = _task;
            }

            _disposeCancellationTokenSource.Cancel();

            try
            {
                await task.ConfigureAwait(false);
            }
            catch
            {
                // suppress
            }
            finally
            {
                _disposeCancellationTokenSource.Dispose();

                lock (_lockObject)
                {
                    _state = State.Disposed;
                }
            }
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().Await(DisposeTimeout);
        }

        public ValueTask<T> Enqueue<T>(Func<CancellationToken, ValueTask<T>> taskFunc, CancellationToken cancellationToken)
        {
            Argument.NotNull(taskFunc);

            lock (_lockObject)
            {
                CheckDisposed();

                var previousTask = _task;
                var task = Task.Factory.StartNew(
                    () => RunAfterPrevious(taskFunc, previousTask, cancellationToken),
                    cancellationToken,
                    TaskCreationOptions.PreferFairness,
                    _taskScheduler).Unwrap();
                _task = task;
                return new ValueTask<T>(task);
            }
        }

        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Previous task exception should be observed on a task returned from Enqueue method")]
        private async Task<T> RunAfterPrevious<T>(Func<CancellationToken, ValueTask<T>> taskFunc, Task previousTask, CancellationToken cancellationToken)
        {
            try
            {
                await previousTask.ConfigureAwait(false);
            }
            catch
            {
                // suppress
            }

            using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCancellationTokenSource.Token);
            return await taskFunc(linkedToken.Token).ConfigureAwait(false);
        }

        private void CheckDisposed()
        {
            if (_state != State.Running)
            {
                throw new ObjectDisposedException(nameof(TaskFlow));
            }
        }

        private enum State
        {
            Running = 0,

            Disposing,

            Disposed,
        }
    }
}