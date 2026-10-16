namespace System.Threading.Tasks.Flow.Internal;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

// These registrations declare constraints. Only the outermost one enters the shared coordinator.
internal class CoordinatedPolicy : ITaskSchedulerEnqueueMiddleware
{
    public Task<T> InvokeAsync<T>(TaskSchedulerEnqueueContext<T> context, TaskSchedulerEnqueueDelegate<T> next) => next(context);
}

internal sealed class ExecutionPolicy(int maximum) : CoordinatedPolicy
{
    public int Maximum { get; } = maximum;
    public int Active { get; set; }
}

internal sealed class AdmissionPolicy(BoundedAdmissionController controller, bool perKey) : CoordinatedPolicy
{
    public BoundedAdmissionController Controller { get; } = controller;
    public bool PerKey { get; } = perKey;
    public int Used { get; set; }
    public int Waiters { get; set; }
}

internal sealed class KeyedCoordinator : CoordinatedPolicy, IDisposable
{
    private readonly object _sync = new object();
    private readonly Dictionary<object, KeyState> _keys;
    private readonly LinkedList<KeyState> _ready = new LinkedList<KeyState>();
    private readonly LinkedList<Submission> _waiters = new LinkedList<Submission>();
    private readonly CancellationToken _stopping;
    private readonly CancellationTokenRegistration _stoppingRegistration;
    private bool _closed;
    private bool _pumping;

    public KeyedCoordinator(Type? keyType, IEqualityComparer<object> comparer, TaskSchedulerMiddlewareLifetime lifetime)
    {
        KeyType = keyType;
        _keys = new Dictionary<object, KeyState>(comparer);
        _stopping = lifetime.StoppingToken;
        _stoppingRegistration = _stopping.Register(Close);
    }

    public Type? KeyType { get; }
    internal int KeyCount { get { lock (_sync) return _keys.Count; } }

    public async Task<T> Schedule<T>(PipelineOperation<T> operation, CoordinatedPolicy[] policies,
        Func<CancellationToken, Task<T>> next, CancellationToken cancellationToken)
    {
        Submission<T> submission;
        lock (_sync)
        {
            var key = operation.Key ?? new object();
            if (!_keys.TryGetValue(key, out var state))
            {
                state = new KeyState(key);
                _keys.Add(key, state);
            }
            state.References++;
            submission = new Submission<T>(state, policies, operation.AdmissionRequest, next) { CallerToken = cancellationToken };
        }
        operation.OnQuiesced(() => Complete(submission));
        operation.OnStarted(() => Started(submission));
        using var cancellation = cancellationToken.Register(() => Cancel(submission));
        lock (_sync)
        {
            if (submission.Settled)
            { }
            else if (_closed)
                Reject(submission, TaskSchedulerAdmissionResult.Closed);
            else if (cancellationToken.IsCancellationRequested)
            { submission.Settled = true; submission.Cancel(); }
            else if (CanAdmit(submission) && !HasEarlierWaiter(submission))
                Admit(submission);
            else if (ShouldReject(submission))
                Reject(submission, TaskSchedulerAdmissionResult.Full);
            else if (WaiterLimitReached(submission))
                Reject(submission, TaskSchedulerAdmissionResult.WaiterLimitReached);
            else
            {
                submission.Waiter = _waiters.AddLast(submission);
                ChangeWaiters(submission, 1);
            }
        }
        Pump();
        return await submission.Task.ConfigureAwait(false);
    }

    private static int Used(Submission submission, AdmissionPolicy policy)
        => policy.PerKey ? Get(submission.Key.Used, policy) : policy.Used;
    private static int Get(Dictionary<AdmissionPolicy, int> counts, AdmissionPolicy policy)
        => counts.TryGetValue(policy, out var value) ? value : 0;
    private static void Change(Dictionary<AdmissionPolicy, int> counts, AdmissionPolicy policy, int delta)
    {
        var value = Get(counts, policy) + delta;
        if (value == 0)
            counts.Remove(policy);
        else
            counts[policy] = value;
    }
    private static bool CanAdmit(Submission submission)
    {
        foreach (var policy in submission.Policies)
            if (policy is AdmissionPolicy admission && Used(submission, admission) >= admission.Controller.Capacity)
                return false;
        return true;
    }
    private bool HasEarlierWaiter(Submission submission)
    {
        foreach (var waiter in _waiters)
            if (waiter.Key == submission.Key || CanAdmit(waiter))
                return true;
        return false;
    }
    private static bool ShouldReject(Submission submission)
    {
        if (submission.Request != null)
            return submission.Request.Mode == TaskSchedulerAdmissionMode.Reject;
        foreach (var policy in submission.Policies)
            if (policy is AdmissionPolicy admission && admission.Controller.AdmissionMode == TaskSchedulerAdmissionMode.Reject)
                return true;
        return false;
    }
    private static bool WaiterLimitReached(Submission submission)
    {
        foreach (var policy in submission.Policies)
            if (policy is AdmissionPolicy admission && admission.Controller.MaxAdmissionWaiters is int maximum &&
                (admission.PerKey ? Get(submission.Key.Waiters, admission) : admission.Waiters) >= maximum)
                return true;
        return false;
    }
    private static void ChangeWaiters(Submission submission, int delta)
    {
        foreach (var policy in submission.Policies)
            if (policy is AdmissionPolicy admission)
            {
                if (admission.PerKey)
                    Change(submission.Key.Waiters, admission, delta);
                else
                    admission.Waiters += delta;
            }
    }
    private void RemoveWaiter(Submission submission)
    {
        if (submission.Waiter == null)
            return;
        _waiters.Remove(submission.Waiter);
        submission.Waiter = null;
        ChangeWaiters(submission, -1);
    }
    private void Admit(Submission submission)
    {
        submission.Accepted = true;
        for (var i = 0; i < submission.Policies.Length; i++)
            if (submission.Policies[i] is AdmissionPolicy admission)
            {
                submission.Reserved[i] = true;
                if (admission.PerKey)
                    Change(submission.Key.Used, admission, 1);
                else
                    admission.Used++;
            }
        submission.Request?.Decide(TaskSchedulerAdmissionResult.Accepted);
        submission.Queued = submission.Key.Queue.AddLast(submission);
        MakeReady(submission.Key);
    }
    private void MakeReady(KeyState key)
    {
        if (!key.Busy && key.Queue.Count != 0 && key.Ready == null)
            key.Ready = _ready.AddLast(key);
    }
    private static void Reject(Submission submission, TaskSchedulerAdmissionResult result)
    {
        submission.Settled = true;
        submission.Request?.Decide(result);
        submission.Reject(result);
    }
    private void Cancel(Submission submission)
    {
        lock (_sync)
        {
            if (submission.Dispatched || submission.Settled)
                return;
            RemoveWaiter(submission);
            if (submission.Queued != null)
                submission.Key.Queue.Remove(submission.Queued);
            submission.Queued = null;
            submission.Settled = true;
            submission.Cancel();
        }
        Pump();
    }
    private void Started(Submission submission)
    {
        lock (_sync)
            ReleaseReservations(submission, pendingOnly: true);
        Pump();
    }
    private static void ReleaseReservations(Submission submission, bool pendingOnly)
    {
        for (var i = 0; i < submission.Policies.Length; i++)
            if (submission.Reserved[i] && submission.Policies[i] is AdmissionPolicy admission &&
                (!pendingOnly || admission.Controller.CapacityMode == TaskSchedulerCapacityMode.Pending))
            {
                submission.Reserved[i] = false;
                if (admission.PerKey)
                    Change(submission.Key.Used, admission, -1);
                else
                    admission.Used--;
            }
    }
    private void Complete(Submission submission)
    {
        lock (_sync)
        {
            ReleaseReservations(submission, pendingOnly: false);
            if (submission.Dispatched)
            {
                submission.Key.Busy = false;
                foreach (var policy in submission.Policies)
                    if (policy is ExecutionPolicy execution)
                        execution.Active--;
            }
            submission.Key.References--;
            MakeReady(submission.Key);
            if (submission.Key.References == 0)
            {
                if (submission.Key.Ready != null)
                    _ready.Remove(submission.Key.Ready);
                _keys.Remove(submission.Key.Value);
            }
        }
        submission.Cleanup();
        Pump();
    }
    private static bool CanDispatch(Submission submission)
    {
        foreach (var policy in submission.Policies)
            if (policy is ExecutionPolicy execution && execution.Active >= execution.Maximum)
                return false;
        return true;
    }
    private void PromoteWaiters()
    {
        var blocked = new HashSet<KeyState>();
        var node = _waiters.First;
        while (node != null)
        {
            var submission = node.Value;
            node = node.Next;
            if (!blocked.Contains(submission.Key) && CanAdmit(submission))
            {
                RemoveWaiter(submission);
                Admit(submission);
            }
            else
                blocked.Add(submission.Key);
        }
    }
    private void Pump()
    {
        lock (_sync)
        {
            if (_pumping || _closed)
                return;
            _pumping = true;
        }
        while (true)
        {
            Submission? dispatch = null;
            lock (_sync)
            {
                if (!_closed)
                {
                    PromoteWaiters();
                    var node = _ready.First;
                    while (node != null)
                    {
                        var key = node.Value;
                        var following = node.Next;
                        if (key.Queue.First == null)
                        {
                            _ready.Remove(node);
                            key.Ready = null;
                        }
                        else if (CanDispatch(key.Queue.First.Value))
                        {
                            dispatch = key.Queue.First.Value;
                            key.Queue.RemoveFirst();
                            dispatch.Queued = null;
                            _ready.Remove(node);
                            key.Ready = null;
                            key.Busy = true;
                            dispatch.Dispatched = true;
                            foreach (var policy in dispatch.Policies)
                                if (policy is ExecutionPolicy execution)
                                    execution.Active++;
                            break;
                        }
                        node = following;
                    }
                }
                if (dispatch == null)
                { _pumping = false; return; }
            }
            dispatch.Dispatch(_stopping);
        }
    }
    private void Close()
    {
        lock (_sync)
        {
            _closed = true;
            while (_waiters.First != null)
            {
                var submission = _waiters.First.Value;
                RemoveWaiter(submission);
                Reject(submission, TaskSchedulerAdmissionResult.Closed);
            }
            foreach (var key in _keys.Values)
            {
                foreach (var submission in key.Queue)
                { submission.Settled = true; submission.Cancel(); }
                key.Queue.Clear();
                key.Ready = null;
            }
            _ready.Clear();
        }
    }
    public void Dispose() => _stoppingRegistration.Dispose();

    private sealed class KeyState(object value)
    {
        public object Value { get; } = value;
        public int References { get; set; }
        public bool Busy { get; set; }
        public LinkedList<Submission> Queue { get; } = new LinkedList<Submission>();
        public LinkedListNode<KeyState>? Ready { get; set; }
        public Dictionary<AdmissionPolicy, int> Used { get; } = new Dictionary<AdmissionPolicy, int>();
        public Dictionary<AdmissionPolicy, int> Waiters { get; } = new Dictionary<AdmissionPolicy, int>();
    }
    private abstract class Submission(KeyState key, CoordinatedPolicy[] policies, AdmissionRequest? request)
    {
        public KeyState Key { get; } = key;
        public CoordinatedPolicy[] Policies { get; } = policies;
        public bool[] Reserved { get; } = new bool[policies.Length];
        public AdmissionRequest? Request { get; } = request;
        public bool Accepted { get; set; }
        public bool Dispatched { get; set; }
        public bool Settled { get; set; }
        public LinkedListNode<Submission>? Waiter { get; set; }
        public LinkedListNode<Submission>? Queued { get; set; }
        public abstract void Dispatch(CancellationToken stopping);
        public abstract void Reject(TaskSchedulerAdmissionResult result);
        public abstract void Cancel();
        public abstract void Cleanup();
    }
    private sealed class Submission<T>(KeyState key, CoordinatedPolicy[] policies,
        AdmissionRequest? request, Func<CancellationToken, Task<T>> next) : Submission(key, policies, request)
    {
        private readonly TaskCompletionSource<T> _completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenSource? _linked;
        private readonly ExecutionContext? _executionContext = ExecutionContext.Capture();
        public Task<T> Task => _completion.Task;
        public override void Reject(TaskSchedulerAdmissionResult result) => _completion.TrySetException(new AdmissionRejectedException(result));
        public override void Cancel()
        {
            if (!Accepted)
                Request?.Cancel();
            _completion.TrySetCanceled();
        }
        public override void Dispatch(CancellationToken stopping)
        {
            if (_executionContext != null)
                ExecutionContext.Run(_executionContext, state => { _ = ExecuteAsync(stopping); }, null);
            else if (ExecutionContext.IsFlowSuppressed())
                _ = ExecuteAsync(stopping);
            else
            {
                // A caller that suppressed flow must not inherit the preceding operation's context.
                using var suppression = ExecutionContext.SuppressFlow();
                _ = ExecuteAsync(stopping);
            }
        }
        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Operation failures are returned to the submitting caller.")]
        private async Task ExecuteAsync(CancellationToken stopping)
        {
            try
            {
                _linked = CancellationTokenSource.CreateLinkedTokenSource(CallerToken, stopping);
                _completion.TrySetResult(await next(_linked.Token).ConfigureAwait(false));
            }
            catch (OperationCanceledException) { _completion.TrySetCanceled(CancellationToken.None); }
            catch (Exception exception) { _completion.TrySetException(exception); }
        }
        public CancellationToken CallerToken { get; set; }
        public override void Cleanup()
        {
            _linked?.Dispose();
            _executionContext?.Dispose();
        }
    }

    internal sealed class Factory(Type? keyType, IEqualityComparer<object> comparer) : ITaskSchedulerMiddlewareFactory
    {
        public ITaskSchedulerMiddleware Create(TaskSchedulerMiddlewareLifetime lifetime) => new KeyedCoordinator(keyType, comparer, lifetime);
    }
}