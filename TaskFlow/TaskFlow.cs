namespace System.Threading.Tasks.Flow
{
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks.Flow.Annotations;

    public sealed partial class TaskFlow : TaskFlowBase
    {
        private Task _task;

        public TaskFlow()
         : this(TaskFlowOptions.Default)
        {
        }

        public TaskFlow(TaskFlowOptions options)
            : base(options)
        {
            _task = Task.CompletedTask;
            Ready();
        }

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

        protected override Task GetInitializationTask()
        {
            return Task.CompletedTask;
        }

        protected override Task GetCompletionTask()
        {
            lock (ThisLock)
            {
                return _task;
            }
        }

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