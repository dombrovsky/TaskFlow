namespace System.Threading.Tasks.Flow;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

/// <summary>Owns middleware registrations and waits for pipeline work before cleaning them up.</summary>
/// <remarks>Flows own their implicit lifetime. Explicit lifetimes are disposed by their caller, before the terminal scheduler.</remarks>
public sealed class TaskSchedulerMiddlewareLifetime : IAsyncDisposable
{
    private static readonly AsyncLocal<Frame?> Current = new AsyncLocal<Frame?>();

    private readonly object _sync;
    private readonly CancellationTokenSource _stopping = new CancellationTokenSource();
    private readonly List<ITaskSchedulerMiddleware?> _owned = new List<ITaskSchedulerMiddleware?>();
    private readonly TaskCompletionSource<bool> _drained = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _active;
    private bool _closed;
    private Task? _shutdown;

    /// <summary>Creates an explicitly owned lifetime for a scheduler without a flow owner.</summary>
    public TaskSchedulerMiddlewareLifetime() : this(new object()) { }

    internal TaskSchedulerMiddlewareLifetime(object sync)
    {
        _sync = sync;
        StoppingToken = _stopping.Token;
    }

    /// <summary>Gets a token canceled when shutdown closes entry, before work is drained.</summary>
    public CancellationToken StoppingToken { get; }

    internal void CheckReentrantDisposal()
    {
        for (var frame = Current.Value; frame != null; frame = frame.Parent)
        {
            if (frame.Lifetime == this)
                throw new InvalidOperationException("A middleware lifetime cannot be disposed from its own submission, factory, or cleanup.");
        }
    }

    internal IDisposable? EnterContextIfNeeded()
    {
        for (var frame = Current.Value; frame != null; frame = frame.Parent)
            if (frame.Lifetime == this)
                return null;
        return EnterContext();
    }

    internal IDisposable EnterContext()
    {
        var previous = Current.Value;
        Current.Value = new Frame(this, previous);
        return Internal.Disposable.Create(() => Current.Value = previous);
    }

    private void ThrowIfClosed()
    {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_closed, this);
#else
        if (_closed)
            throw new ObjectDisposedException(nameof(TaskSchedulerMiddlewareLifetime));
#endif
    }

    internal void CheckRegistrationOpen()
    {
        lock (_sync)
        { ThrowIfClosed(); }
    }

    internal void Enter()
    {
        lock (_sync)
        {
            ThrowIfClosed();
            _active++;
        }
    }

    internal void Exit()
    {
        lock (_sync)
        {
            _active--;
            if (_closed && _active == 0)
                _drained.TrySetResult(true);
        }
    }

    internal ITaskSchedulerMiddleware Register(ITaskSchedulerMiddlewareFactory factory)
    {
        int index;
        lock (_sync)
        {
            Enter();
            index = _owned.Count;
            _owned.Add(null);
        }

        try
        {
            using var context = EnterContext();
            var middleware = factory.Create(this) ?? throw new InvalidOperationException("The middleware factory returned null.");
            lock (_sync)
            {
                // Even a factory racing shutdown is attached and cleaned up by this owner.
                _owned[index] = middleware;
                ThrowIfClosed();
            }
            TaskSchedulerMiddlewareExtensions.ValidateMiddleware(middleware);
            return middleware;
        }
        finally
        {
            Exit();
        }
    }

    internal void Close()
    {
        lock (_sync)
        {
            _closed = true;
            if (_active == 0)
                _drained.TrySetResult(true);
        }
    }

    internal void SignalStopping() => _stopping.Cancel();

    internal Task WaitForSubmissionsAsync() => _drained.Task;

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "All owned cleanups must be attempted and their failures aggregated.")]
    internal async Task CleanupAsync()
    {
        using var context = EnterContext();
        var errors = new List<Exception>();
        for (var index = _owned.Count - 1; index >= 0; index--)
        {
            try
            {
                if (_owned[index] is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                else if (_owned[index] is IDisposable disposable)
                    disposable.Dispose();
            }
            catch (Exception exception) { errors.Add(exception); }
        }
        _owned.Clear();
        _stopping.Dispose();
        if (errors.Count != 0)
            throw new AggregateException(errors);
    }

    /// <summary>Closes entry, signals stopping, drains work, and attempts cleanup once in reverse registration order.</summary>
    public ValueTask DisposeAsync()
    {
        CheckReentrantDisposal();
        TaskCompletionSource<bool> completion;
        lock (_sync)
        {
            if (_shutdown != null)
                return new ValueTask(_shutdown);
            Close();
            completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _shutdown = completion.Task;
        }
        _ = ShutdownAsync(completion);
        return new ValueTask(completion.Task);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Shutdown reports errors through its shared task after attempting every phase.")]
    private async Task ShutdownAsync(TaskCompletionSource<bool> completion)
    {
        var errors = new List<Exception>();
        try
        { SignalStopping(); }
        catch (Exception exception) { errors.Add(exception); }
        await WaitForSubmissionsAsync().ConfigureAwait(false);
        try
        { await CleanupAsync().ConfigureAwait(false); }
        catch (Exception exception) { errors.Add(exception); }
        if (errors.Count == 0)
            completion.TrySetResult(true);
        else
            completion.TrySetException(new AggregateException(errors));
    }

    private sealed class Frame
    {
        public Frame(TaskSchedulerMiddlewareLifetime lifetime, Frame? parent) { Lifetime = lifetime; Parent = parent; }
        public TaskSchedulerMiddlewareLifetime Lifetime { get; }
        public Frame? Parent { get; }
    }
}