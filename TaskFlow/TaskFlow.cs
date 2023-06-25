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

        public override ValueTask<T> Enqueue<T>(Func<CancellationToken, ValueTask<T>> taskFunc, CancellationToken cancellationToken)
        {
            Argument.NotNull(taskFunc);

            lock (ThisLock)
            {
                CheckDisposed();

                var previousTask = _task;
                var task = Task.Factory.StartNew(
                    () => RunAfterPrevious(taskFunc, previousTask, cancellationToken),
                    cancellationToken,
                    TaskCreationOptions.PreferFairness,
                    Options.TaskScheduler).Unwrap();
                _task = task;
                return new ValueTask<T>(task);
            }
        }

        protected override Task GetInitializationTask()
        {
            return Task.CompletedTask;
        }

        protected override Task GetCompletionTask()
        {
            return _task;
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
                // suppressed
            }

            using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, CompletionToken);
            return await taskFunc(linkedToken.Token).ConfigureAwait(false);
        }
    }
}