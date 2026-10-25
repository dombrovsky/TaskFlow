namespace System.Threading.Tasks.Flow.Internal;

internal sealed class AdmissionRequest
{
    // These sources are completed once; requests expose only their reusable tasks.
    private static readonly TaskCompletionSource<TaskSchedulerAdmissionResult>[] CompletedDecisions =
    {
        CreateCompletedDecision(TaskSchedulerAdmissionResult.Accepted),
        CreateCompletedDecision(TaskSchedulerAdmissionResult.Full),
        CreateCompletedDecision(TaskSchedulerAdmissionResult.WaiterLimitReached),
        CreateCompletedDecision(TaskSchedulerAdmissionResult.Closed)
    };

    private readonly object _sync = new object();

    private TaskCompletionSource<TaskSchedulerAdmissionResult>? _decisionSource;
    private bool _decided;
    private bool _quiesced;
    private bool _callerCompleted;
    private bool _canceled;
    private Exception? _failure;

    public AdmissionRequest(BoundedAdmissionController controller, TaskSchedulerAdmissionMode mode, bool synchronous = false)
    {
        Controller = controller;
        Mode = mode;
        Synchronous = synchronous;
    }
    public BoundedAdmissionController Controller { get; }
    public TaskSchedulerAdmissionMode Mode { get; }
    public bool Synchronous { get; }

    public Task<TaskSchedulerAdmissionResult> Decision
    {
        get
        {
            lock (_sync)
            { return GetDecisionSource().Task; }
        }
    }

    public void Decide(TaskSchedulerAdmissionResult result)
    {
        lock (_sync)
        {
            if (_decided)
                return;
            _decided = true;
            if (_decisionSource != null)
                _decisionSource.TrySetResult(result);
            else
                _decisionSource = CompletedDecisions[(int)result];
        }
    }

    public void Cancel()
    {
        lock (_sync)
        {
            if (_decided)
                return;
            _decided = true;
            GetDecisionSource().TrySetCanceled();
        }
    }

    // Called under _sync. Reuse a completed source or create a per-request promise on demand.
    private TaskCompletionSource<TaskSchedulerAdmissionResult> GetDecisionSource()
    {
        if (_decisionSource == null)
        {
            _decisionSource = new TaskCompletionSource<TaskSchedulerAdmissionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        return _decisionSource;
    }

    private static TaskCompletionSource<TaskSchedulerAdmissionResult> CreateCompletedDecision(TaskSchedulerAdmissionResult result)
    {
        var source = new TaskCompletionSource<TaskSchedulerAdmissionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult(result);
        return source;
    }

    public void OnQuiesced()
    {
        lock (_sync)
        { _quiesced = true; SettleUnreached(); }
    }

    private void SettleUnreached()
    {
        if (!_quiesced || !_callerCompleted || _decided)
            return;
        _decided = true;
        if (_canceled)
            GetDecisionSource().TrySetCanceled();
        else
        {
            var failure = _failure ?? new InvalidOperationException("The pipeline completed without reaching the selected admission registration.");
            GetDecisionSource().TrySetException(failure);
        }
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