namespace System.Threading.Tasks.Flow.Internal;

using System.Collections.Generic;

internal sealed class BoundedAdmissionMiddleware : ITaskSchedulerEnqueueMiddleware, IDisposable
{
    private static readonly WaitCallback DrainCallback = static state => ((BoundedAdmissionMiddleware)state!).Drain(onWorker: true);

    private readonly object _sync = new object();
    private readonly LinkedList<Submission> _waiters = new LinkedList<Submission>();
    private readonly Queue<Submission> _dispatch = new Queue<Submission>();
    private readonly CancellationTokenRegistration _stoppingRegistration;

    private int _used;
    // _sync protects ownership and both queues. True means one queued OR active drain.
    // Handoff keeps ownership; only an empty-queue observation under _sync publishes idle.
    // Invoke continuations outside _sync. A racing arrival joins this owner or claims the next.
    private bool _pumping;
    private bool _closed;

    public BoundedAdmissionMiddleware(BoundedAdmissionController controller, TaskSchedulerMiddlewareLifetime lifetime)
    {
        Controller = controller;
        _stoppingRegistration = lifetime.StoppingToken.Register(Close);
    }

    public BoundedAdmissionController Controller { get; }

    public async Task<T> InvokeAsync<T>(TaskSchedulerEnqueueContext<T> context, TaskSchedulerEnqueueDelegate<T> next)
    {
        var request = context.Operation.AdmissionRequest;
        if (request?.Controller != Controller)
            request = null;
        var submission = new Submission<T>(this, context, next, request);
        context.Operation.OnQuiesced(submission.Quiesced);
        using var cancellation = context.CancellationToken.Register(() => Cancel(submission));
        var ownsDrain = false;
        lock (_sync)
        {
            if (submission.Settled)
            { }
            else if (_closed)
                Reject(submission, TaskSchedulerAdmissionResult.Closed);
            else if (_used < Controller.Capacity && _waiters.Count == 0)
                Reserve(submission);
            else if ((request?.Mode ?? Controller.AdmissionMode) == TaskSchedulerAdmissionMode.Reject)
                Reject(submission, TaskSchedulerAdmissionResult.Full);
            else if (Controller.MaxAdmissionWaiters is int maximum && _waiters.Count >= maximum)
                Reject(submission, TaskSchedulerAdmissionResult.WaiterLimitReached);
            else
                submission.Waiter = _waiters.AddLast(submission);

            // Claim inline ownership before releasing the lock. An immediate legacy operation
            // is guaranteed to dispatch on this stack, so it needs no context snapshot.
            if (!_pumping && _dispatch.Count != 0)
            {
                _pumping = true;
                ownsDrain = true;
            }
            if (!submission.Settled && (request?.Synchronous == true || !ownsDrain || _dispatch.Peek() != submission))
                submission.CaptureContext();
        }
        if (ownsDrain)
            Drain(onWorker: false, defer: request?.Synchronous == true);
        return await submission.Task.ConfigureAwait(false);
    }

    private void Reserve(Submission submission)
    {
        _used++;
        submission.Reserved = true;
        submission.Request?.Decide(TaskSchedulerAdmissionResult.Accepted);
        _dispatch.Enqueue(submission);
    }

    private static void Reject(Submission submission, TaskSchedulerAdmissionResult reason)
    {
        submission.Settled = true;
        submission.Request?.Decide(reason);
        submission.Reject(reason);
    }

    private void Cancel(Submission submission)
    {
        lock (_sync)
        {
            if (submission.Reserved || submission.Settled)
                return;
            if (submission.Waiter != null)
                _waiters.Remove(submission.Waiter);
            submission.Settled = true;
            submission.Request?.Cancel();
            submission.Cancel();
        }
    }

    private void Release(bool defer)
    {
        lock (_sync)
        {
            _used--;
            if (!_closed && _waiters.First != null)
            {
                var first = _waiters.First.Value;
                _waiters.RemoveFirst();
                first.Waiter = null;
                Reserve(first);
            }
        }
        Pump(defer);
    }

    private void Pump(bool defer = false)
    {
        lock (_sync)
        {
            if (_pumping || _dispatch.Count == 0)
                return;
            _pumping = true;
        }
        Drain(onWorker: false, defer);
    }

    // _pumping owns both the queued worker and its drain. Publish idle under the queue lock.
    private void Drain(bool onWorker, bool defer = false)
    {
        while (true)
        {
            Submission? next = null;
            lock (_sync)
            {
                if (_dispatch.Count == 0)
                    _pumping = false;
                else if (!onWorker && (defer || _dispatch.Peek().RequiresWorker))
                {
                    _ = ThreadPool.UnsafeQueueUserWorkItem(DrainCallback, this);
                    return;
                }
                else
                    next = _dispatch.Dequeue();
            }
            if (next == null)
                return;
            // One dispatcher invokes continuations in FIFO order, outside the admission lock.
            next.Dispatch(onWorker);
        }
    }

    private void Close()
    {
        lock (_sync)
        {
            _closed = true;
            foreach (var waiter in _waiters)
                Reject(waiter, TaskSchedulerAdmissionResult.Closed);
            _waiters.Clear();
        }
    }

    public void Dispose() => _stoppingRegistration.Dispose();

    private abstract class Submission
    {
        protected Submission(AdmissionRequest? request)
        {
            Request = request;
            RequiresWorker = request?.Synchronous == true;
        }
        public bool RequiresWorker { get; }
        public AdmissionRequest? Request { get; }
        public LinkedListNode<Submission>? Waiter { get; set; }
        public bool Reserved { get; set; }
        public bool Settled { get; set; }
        public abstract void Dispatch(bool onWorker);
        public abstract void Reject(TaskSchedulerAdmissionResult result);
        public abstract void Cancel();
    }

    private sealed class Submission<T> : Submission
    {
        private readonly BoundedAdmissionMiddleware _owner;
        private readonly TaskSchedulerEnqueueContext<T> _context;
        private readonly TaskSchedulerEnqueueDelegate<T> _next;
        private readonly TaskCompletionSource<T> _completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        private ExecutionContext? _executionContext;
        private int _released;

        public Submission(BoundedAdmissionMiddleware owner, TaskSchedulerEnqueueContext<T> context, TaskSchedulerEnqueueDelegate<T> next, AdmissionRequest? request) : base(request)
        {
            _owner = owner;
            _context = context;
            _next = next;
        }

        public Task<T> Task => _completion.Task;
        public override void Reject(TaskSchedulerAdmissionResult result) => _completion.TrySetException(new AdmissionRejectedException(result));
        public override void Cancel() => _completion.TrySetCanceled();
        public void CaptureContext() => _executionContext = ExecutionContext.Capture();

        public override void Dispatch(bool onWorker)
        {
            if (_owner.Controller.CapacityMode == TaskSchedulerCapacityMode.Pending)
                _context.Operation.OnStarted(ReleaseOnce);
            // Legacy-only inline drains retain the pump caller's context. A shared worker
            // must restore each queued producer instead of borrowing the TrySubmit owner's context.
            if (!onWorker)
                _ = ForwardCompletionAsync();
            else if (_executionContext != null)
                ExecutionContext.Run(_executionContext, static state => { _ = ((Submission<T>)state!).ForwardCompletionAsync(); }, this);
            else
                // A suppressed producer starts from the context-free worker.
                _ = ForwardCompletionAsync();
        }

        public void Quiesced()
        {
            _executionContext?.Dispose();
            if (Reserved)
                ReleaseOnce();
        }

        private void ReleaseOnce()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                _owner.Release(Request?.Synchronous == true);
        }

        // The submission awaits _completion.Task. Forward every outcome there so the drain
        // can dispatch other operations without awaiting this bridge task.
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Propagates every operation failure through its completion task.")]
        private async Task ForwardCompletionAsync()
        {
            try
            {
                _completion.TrySetResult(await _next(_context).ConfigureAwait(false));
            }
            catch (OperationCanceledException) { _completion.TrySetCanceled(); }
            catch (Exception exception) { _completion.TrySetException(exception); }
        }
    }
}