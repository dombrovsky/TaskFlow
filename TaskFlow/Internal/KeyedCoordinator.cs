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

internal abstract class SchedulingCoordinator : CoordinatedPolicy, IDisposable
{
    public abstract bool RequiresKey { get; }
    internal abstract int KeyCount { get; }
    internal abstract int ScheduledDrainCount { get; }
    internal Action? DrainIdle { get; set; }
    public abstract Task<T> Schedule<T>(PipelineOperation<T> operation, CoordinatedPolicy[] policies,
        Func<CancellationToken, Task<T>> next, CancellationToken cancellationToken);
    public abstract void Dispose();
}

internal interface IKeyedCoordinator<TKey> where TKey : notnull
{
    void ValidateKey(TKey key);
}

internal class KeyedCoordinator<TKey> : SchedulingCoordinator, IKeyedCoordinator<TKey> where TKey : notnull
{
    private static readonly WaitCallback DrainCallback = static state => ((KeyedCoordinator<TKey>)state!).Drain(onWorker: true);

    private readonly object _sync = new object();
    private readonly Dictionary<TKey, KeyState> _keys;
    private readonly LinkedList<KeyState> _ready = new LinkedList<KeyState>();
    private readonly LinkedList<Submission> _waiters = new LinkedList<Submission>();
    private readonly CancellationToken _stopping;
    private readonly CancellationTokenRegistration _stoppingRegistration;

    private int _scheduledDrains;
    private bool _closed;
    // _sync protects ready/waiter queues, accounting, and the single queued OR active owner.
    // A handoff never clears ownership. Only observing no runnable work under _sync retires it;
    // a racing arrival joins that owner or claims the next. Dispatch callbacks run outside _sync.
    private bool _pumping;

    public KeyedCoordinator(IEqualityComparer<TKey> comparer, TaskSchedulerMiddlewareLifetime lifetime)
    {
        _keys = new Dictionary<TKey, KeyState>(comparer);
        _stopping = lifetime.StoppingToken;
        _stoppingRegistration = _stopping.Register(Close);
    }

    public override bool RequiresKey => true;
    internal override int ScheduledDrainCount { get { lock (_sync) return _scheduledDrains; } }
    internal override int KeyCount { get { lock (_sync) return _keys.Count; } }

    public void ValidateKey(TKey key)
    {
        if (key is null)
            throw new ArgumentNullException(nameof(key));
    }

    protected virtual TKey GetKey(PipelineOperation operation)
        => operation is IKeyedOperation<TKey> keyed ? keyed.Key
            : throw new InvalidOperationException("A matching typed keyed operation is required.");

    public override async Task<T> Schedule<T>(PipelineOperation<T> operation, CoordinatedPolicy[] policies,
        Func<CancellationToken, Task<T>> next, CancellationToken cancellationToken)
    {
        Submission<T> submission;
        lock (_sync)
        {
            var key = GetKey(operation);
            if (!_keys.TryGetValue(key, out var state))
            {
                state = new KeyState(key);
                _keys.Add(key, state);
            }
            state.References++;
            submission = new Submission<T>(state, policies, operation.AdmissionRequest, next, _stopping) { CallerToken = cancellationToken };
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
        Pump(defer: operation.AdmissionRequest?.Synchronous == true);
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
        Pump(defer: submission.Request?.Synchronous == true);
    }
    private void Started(Submission submission)
    {
        lock (_sync)
            ReleaseReservations(submission, pendingOnly: true);
        Pump(defer: submission.Request?.Synchronous == true);
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
        Pump(defer: submission.Request?.Synchronous == true);
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
        if (_waiters.First == null)
            return;
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
    // Called under _sync after claiming ownership; the worker starts without a producer context.
    private void QueueDrain()
    {
        _scheduledDrains++;
        _ = ThreadPool.UnsafeQueueUserWorkItem(DrainCallback, this);
    }

    private void Pump(bool defer = false)
    {
        lock (_sync)
        {
            if (_pumping || _closed)
                return;
            _pumping = true;
        }
        Drain(onWorker: false, defer);
    }
    private void Drain(bool onWorker, bool defer = false)
    {
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
                            if (!onWorker && (defer || key.Queue.First.Value.RequiresWorker))
                            {
                                QueueDrain();
                                return;
                            }
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
                    _pumping = false;
            }
            if (dispatch == null)
            {
                if (onWorker)
                    DrainIdle?.Invoke();
                return;
            }
            dispatch.Dispatch(onWorker);
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
    public override void Dispose() => _stoppingRegistration.Dispose();

    private sealed class KeyState(TKey value)
    {
        public TKey Value { get; } = value;
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
        public bool RequiresWorker { get; } = request?.Synchronous == true;
        public bool Accepted { get; set; }
        public bool Dispatched { get; set; }
        public bool Settled { get; set; }
        public LinkedListNode<Submission>? Waiter { get; set; }
        public LinkedListNode<Submission>? Queued { get; set; }
        public abstract void Dispatch(bool onWorker);
        public abstract void Reject(TaskSchedulerAdmissionResult result);
        public abstract void Cancel();
        public abstract void Cleanup();
    }
    private sealed class Submission<T>(KeyState key, CoordinatedPolicy[] policies,
        AdmissionRequest? request, Func<CancellationToken, Task<T>> next, CancellationToken stopping) : Submission(key, policies, request)
    {
        private readonly TaskCompletionSource<T> _completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ExecutionContext? _executionContext = ExecutionContext.Capture();

        private CancellationTokenSource? _linked;
        public Task<T> Task => _completion.Task;
        public override void Reject(TaskSchedulerAdmissionResult result) => _completion.TrySetException(new AdmissionRejectedException(result));
        public override void Cancel()
        {
            if (!Accepted)
                Request?.Cancel();
            _completion.TrySetCanceled();
        }
        public override void Dispatch(bool onWorker)
        {
            if (_executionContext != null)
                ExecutionContext.Run(_executionContext, static state => { _ = ((Submission<T>)state!).ForwardCompletionAsync(); }, this);
            else if (onWorker || ExecutionContext.IsFlowSuppressed())
                _ = ForwardCompletionAsync();
            else
            {
                // Preserve legacy inline dispatch: suppression prevents the terminal from
                // capturing the current pump caller, without introducing a worker handoff.
                using var suppression = ExecutionContext.SuppressFlow();
                _ = ForwardCompletionAsync();
            }
        }
        // The submission awaits _completion.Task. Forward every outcome there so the drain
        // can dispatch other operations without awaiting this bridge task.
        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Operation failures are returned to the submitting caller.")]
        private async Task ForwardCompletionAsync()
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

    internal sealed class Factory(IEqualityComparer<TKey> comparer) : ITaskSchedulerMiddlewareFactory
    {
        public ITaskSchedulerMiddleware Create(TaskSchedulerMiddlewareLifetime lifetime) => new KeyedCoordinator<TKey>(comparer, lifetime);
    }
}

// Unkeyed operations need independent lanes, but no object-valued routing or per-operation key allocation.
internal sealed class ConcurrentCoordinator(TaskSchedulerMiddlewareLifetime lifetime)
    : KeyedCoordinator<long>(EqualityComparer<long>.Default, lifetime)
{
    private long _nextKey;
    public override bool RequiresKey => false;
    protected override long GetKey(PipelineOperation operation) => unchecked(_nextKey++);

    internal new sealed class Factory : ITaskSchedulerMiddlewareFactory
    {
        public ITaskSchedulerMiddleware Create(TaskSchedulerMiddlewareLifetime lifetime) => new ConcurrentCoordinator(lifetime);
    }
}