namespace System.Threading.Tasks.Flow.Internal
{
    internal sealed class AdmissionRequest
    {
        private readonly object _sync = new object();
        private readonly TaskCompletionSource<TaskSchedulerAdmissionResult> _decision = new TaskCompletionSource<TaskSchedulerAdmissionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _quiesced;
        private bool _callerCompleted;
        private bool _canceled;
        private Exception? _failure;

        public AdmissionRequest(BoundedAdmissionController controller, TaskSchedulerAdmissionMode mode) { Controller = controller; Mode = mode; }
        public BoundedAdmissionController Controller { get; }
        public TaskSchedulerAdmissionMode Mode { get; }
        public Task<TaskSchedulerAdmissionResult> Decision => _decision.Task;
        public void Decide(TaskSchedulerAdmissionResult result) => _decision.TrySetResult(result);
        public void Cancel() => _decision.TrySetCanceled();

        public void OnQuiesced()
        {
            lock (_sync) { _quiesced = true; SettleUnreached(); }
        }

        private void SettleUnreached()
        {
            if (!_quiesced || !_callerCompleted || _decision.Task.IsCompleted) return;
            if (_canceled) _decision.TrySetCanceled();
            else _decision.TrySetException(_failure ?? new InvalidOperationException("The pipeline completed without reaching the selected admission registration."));
        }

        public void RecordCallerOutcome(Exception? failure)
        {
            lock (_sync)
            {
                _failure = failure;
                _canceled = failure is OperationCanceledException;
                _callerCompleted = true;
                SettleUnreached();
            }
        }
    }
}
