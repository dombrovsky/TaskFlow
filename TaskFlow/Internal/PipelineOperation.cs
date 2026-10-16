namespace System.Threading.Tasks.Flow.Internal;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

internal abstract class PipelineOperation
{
    private readonly object?[] _localStates;
    private readonly object _localStatesLock = new object();

    private int _completionClaimed;
    private int _activities = 1;
    private bool _started;
    private List<Action>? _onStarted;
    private List<Action>? _onQuiesced;

    public Action? LifetimeExit { get; set; }

    public AdmissionRequest? AdmissionRequest { get; set; }
    public object? Key { get; set; }
    public bool Coordinated { get; set; }

    public void AddActivity()
    {
        lock (_localStatesLock)
        {
            if (_activities == 0)
                throw new InvalidOperationException("A middleware continuation cannot run after its operation has settled.");
            _activities++;
        }
    }

    public void ReleaseActivity()
    {
        List<Action>? callbacks = null;
        var quiesced = false;
        lock (_localStatesLock)
        {
            if (--_activities == 0)
            {
                quiesced = true;
                callbacks = _onQuiesced;
                _onQuiesced = null;
                _onStarted = null;
            }
        }
        if (callbacks != null)
        {
            foreach (var callback in callbacks)
            {
                callback();
            }
        }
        if (quiesced)
            LifetimeExit?.Invoke();
    }

    public void OnQuiesced(Action callback)
    {
        lock (_localStatesLock)
        {
            if (_activities != 0)
            {
                (_onQuiesced ??= new List<Action>()).Add(callback);
                return;
            }
        }
        callback();
    }

    public void OnStarted(Action callback)
    {
        lock (_localStatesLock)
        {
            if (!_started)
            {
                (_onStarted ??= new List<Action>()).Add(callback);
                return;
            }
        }
        callback();
    }

    public void MarkStarted()
    {
        List<Action>? callbacks;
        lock (_localStatesLock)
        {
            if (_started)
                return;
            _started = true;
            callbacks = _onStarted;
            _onStarted = null;
        }
        if (callbacks != null)
        {
            foreach (var callback in callbacks)
            {
                callback();
            }
        }
    }

    protected PipelineOperation(object? state, int registrationCount, CancellationToken callerCancellationToken)
    {
        State = state;
        CallerCancellationToken = callerCancellationToken;
        ProducerCancellationToken = callerCancellationToken;
        _localStates = new object?[registrationCount];
    }

    public object? State { get; }
    public CancellationToken CallerCancellationToken { get; }
    public CancellationToken ProducerCancellationToken { get; set; }

    public T? GetLocalState<T>(int index) where T : class
    {
        var value = Volatile.Read(ref _localStates[index]);
        if (value == null)
        {
            return null;
        }

        return value as T ?? throw new InvalidOperationException("The middleware registration-local state has a different type.");
    }

    public T GetOrCreateLocalState<T>(int index, Func<T> factory) where T : class
    {
        var existing = GetLocalState<T>(index);
        if (existing != null)
        {
            return existing;
        }

        lock (_localStatesLock)
        {
            existing = GetLocalState<T>(index);
            if (existing != null)
            {
                return existing;
            }

            var created = factory() ?? throw new InvalidOperationException("A registration-local state factory cannot return null.");
            _localStates[index] = created;
            return created;
        }
    }

    public bool TryClaimCompletion() => Interlocked.CompareExchange(ref _completionClaimed, 1, 0) == 0;
}

internal sealed class PipelineOperation<TResult> : PipelineOperation
{
    public PipelineOperation(
        Func<object?, CancellationToken, ValueTask<TResult>> taskFunc,
        object? state,
        int registrationCount,
        AnnotationScope? finalAnnotations,
        CancellationToken callerCancellationToken)
        : base(state, registrationCount, callerCancellationToken)
    {
        TaskFunc = taskFunc;
        FinalAnnotations = finalAnnotations;
    }

    public Func<object?, CancellationToken, ValueTask<TResult>> TaskFunc { get; }
    public AnnotationScope? FinalAnnotations { get; }
}