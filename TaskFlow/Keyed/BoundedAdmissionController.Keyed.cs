namespace System.Threading.Tasks.Flow;

using System.Threading.Tasks.Flow.Annotations;
using System.Threading.Tasks.Flow.Internal;

public sealed partial class BoundedAdmissionController
{
    /// <summary>Submits through a keyed pipeline, returning the combined admission decision separately from completion.</summary>
    public TaskSchedulerSubmission<TResult> Submit<TKey, TState, TResult>(ITaskScheduler scheduler, TKey key,
        Func<TKey, TState, CancellationToken, ValueTask<TResult>> taskFunc, TState state,
        TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        ValidateMode(admissionMode);
        var pipeline = KeyedTaskSchedulerExtensions.Validate(scheduler, key);
        if (pipeline.CountController(this) != 1)
            throw new ArgumentException("The pipeline must contain exactly one registration of this controller.", nameof(scheduler));
        var request = new AdmissionRequest(this, admissionMode);
        var completion = pipeline.Submit((s, token) => taskFunc(key, (TState)s!, token), state, request, key, cancellationToken);
        return new TaskSchedulerSubmission<TResult>(request.Decision, completion);
    }

    /// <summary>Submits through a keyed pipeline, returning the combined admission decision separately from completion.</summary>
    public TaskSchedulerSubmission<TResult> Submit<TKey, TState, TResult>(ITaskScheduler scheduler, TKey key,
        Func<TKey, TState, CancellationToken, ValueTask<TResult>> taskFunc, TState state,
        TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => Submit<TKey, TState, TResult>(scheduler, key, taskFunc, state, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed Task operation with typed state.</summary>
    public TaskSchedulerSubmission<TResult> Submit<TKey, TState, TResult>(ITaskScheduler scheduler, TKey key,
        Func<TKey, TState, CancellationToken, Task<TResult>> taskFunc, TState state,
        TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return Submit(scheduler, key, (TKey k, TState s, CancellationToken token) => new ValueTask<TResult>(taskFunc(k, s, token)), state, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed Task operation with typed state.</summary>
    public TaskSchedulerSubmission<TResult> Submit<TKey, TState, TResult>(ITaskScheduler scheduler, TKey key,
        Func<TKey, TState, CancellationToken, Task<TResult>> taskFunc, TState state,
        TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => Submit<TKey, TState, TResult>(scheduler, key, taskFunc, state, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed Action operation.</summary>
    public TaskSchedulerSubmission Submit<TKey>(ITaskScheduler scheduler, TKey key, Action taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return Submit<TKey, object?, Unit>(scheduler, key, (TKey k, object? _, CancellationToken token) => { taskFunc(); return new ValueTask<Unit>(default(Unit)); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed Action operation.</summary>
    public TaskSchedulerSubmission Submit<TKey>(ITaskScheduler scheduler, TKey key, Action taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => Submit<TKey>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed synchronous operation.</summary>
    public TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(ITaskScheduler scheduler, TKey key, Func<TResult> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return Submit<TKey, object?, TResult>(scheduler, key, (TKey k, object? _, CancellationToken token) => { return new ValueTask<TResult>(taskFunc()); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed synchronous operation.</summary>
    public TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(ITaskScheduler scheduler, TKey key, Func<TResult> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => Submit<TKey, TResult>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed Task operation.</summary>
    public TaskSchedulerSubmission Submit<TKey>(ITaskScheduler scheduler, TKey key, Func<Task> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return Submit<TKey, object?, Unit>(scheduler, key, async ValueTask<Unit> (TKey k, object? _, CancellationToken token) => { await taskFunc().ConfigureAwait(true); return default(Unit); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed Task operation.</summary>
    public TaskSchedulerSubmission Submit<TKey>(ITaskScheduler scheduler, TKey key, Func<Task> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => Submit<TKey>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed Task operation.</summary>
    public TaskSchedulerSubmission Submit<TKey>(ITaskScheduler scheduler, TKey key, Func<CancellationToken, Task> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return Submit<TKey, object?, Unit>(scheduler, key, async ValueTask<Unit> (TKey k, object? _, CancellationToken token) => { await taskFunc(token).ConfigureAwait(true); return default(Unit); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed Task operation.</summary>
    public TaskSchedulerSubmission Submit<TKey>(ITaskScheduler scheduler, TKey key, Func<CancellationToken, Task> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => Submit<TKey>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed Task result-producing operation.</summary>
    public TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(ITaskScheduler scheduler, TKey key, Func<Task<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return Submit<TKey, object?, TResult>(scheduler, key, (TKey k, object? _, CancellationToken token) => { return new ValueTask<TResult>(taskFunc()); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed Task result-producing operation.</summary>
    public TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(ITaskScheduler scheduler, TKey key, Func<Task<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => Submit<TKey, TResult>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed Task result-producing operation.</summary>
    public TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(ITaskScheduler scheduler, TKey key, Func<CancellationToken, Task<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return Submit<TKey, object?, TResult>(scheduler, key, (TKey k, object? _, CancellationToken token) => { return new ValueTask<TResult>(taskFunc(token)); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed Task result-producing operation.</summary>
    public TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(ITaskScheduler scheduler, TKey key, Func<CancellationToken, Task<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => Submit<TKey, TResult>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed ValueTask operation.</summary>
    public TaskSchedulerSubmission Submit<TKey>(ITaskScheduler scheduler, TKey key, Func<ValueTask> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return Submit<TKey, object?, Unit>(scheduler, key, async ValueTask<Unit> (TKey k, object? _, CancellationToken token) => { await taskFunc().ConfigureAwait(true); return default(Unit); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed ValueTask operation.</summary>
    public TaskSchedulerSubmission Submit<TKey>(ITaskScheduler scheduler, TKey key, Func<ValueTask> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => Submit<TKey>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed ValueTask operation.</summary>
    public TaskSchedulerSubmission Submit<TKey>(ITaskScheduler scheduler, TKey key, Func<CancellationToken, ValueTask> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return Submit<TKey, object?, Unit>(scheduler, key, async ValueTask<Unit> (TKey k, object? _, CancellationToken token) => { await taskFunc(token).ConfigureAwait(true); return default(Unit); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed ValueTask operation.</summary>
    public TaskSchedulerSubmission Submit<TKey>(ITaskScheduler scheduler, TKey key, Func<CancellationToken, ValueTask> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => Submit<TKey>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed ValueTask result-producing operation.</summary>
    public TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(ITaskScheduler scheduler, TKey key, Func<ValueTask<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return Submit<TKey, object?, TResult>(scheduler, key, (TKey k, object? _, CancellationToken token) => { return taskFunc(); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed ValueTask result-producing operation.</summary>
    public TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(ITaskScheduler scheduler, TKey key, Func<ValueTask<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => Submit<TKey, TResult>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed ValueTask result-producing operation.</summary>
    public TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(ITaskScheduler scheduler, TKey key, Func<CancellationToken, ValueTask<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return Submit<TKey, object?, TResult>(scheduler, key, (TKey k, object? _, CancellationToken token) => { return taskFunc(token); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed ValueTask result-producing operation.</summary>
    public TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(ITaskScheduler scheduler, TKey key, Func<CancellationToken, ValueTask<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => Submit<TKey, TResult>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed Action operation.</summary>
    public TaskSchedulerSubmission Submit<TKey>(ITaskScheduler scheduler, TKey key, Action<TKey> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return Submit<TKey, object?, Unit>(scheduler, key, (TKey k, object? _, CancellationToken token) => { taskFunc(k); return new ValueTask<Unit>(default(Unit)); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed Action operation.</summary>
    public TaskSchedulerSubmission Submit<TKey>(ITaskScheduler scheduler, TKey key, Action<TKey> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => Submit<TKey>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed synchronous operation.</summary>
    public TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(ITaskScheduler scheduler, TKey key, Func<TKey, TResult> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return Submit<TKey, object?, TResult>(scheduler, key, (TKey k, object? _, CancellationToken token) => { return new ValueTask<TResult>(taskFunc(k)); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed synchronous operation.</summary>
    public TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(ITaskScheduler scheduler, TKey key, Func<TKey, TResult> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => Submit<TKey, TResult>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed Task operation.</summary>
    public TaskSchedulerSubmission Submit<TKey>(ITaskScheduler scheduler, TKey key, Func<TKey, Task> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return Submit<TKey, object?, Unit>(scheduler, key, async ValueTask<Unit> (TKey k, object? _, CancellationToken token) => { await taskFunc(k).ConfigureAwait(true); return default(Unit); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed Task operation.</summary>
    public TaskSchedulerSubmission Submit<TKey>(ITaskScheduler scheduler, TKey key, Func<TKey, Task> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => Submit<TKey>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed Task operation.</summary>
    public TaskSchedulerSubmission Submit<TKey>(ITaskScheduler scheduler, TKey key, Func<TKey, CancellationToken, Task> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return Submit<TKey, object?, Unit>(scheduler, key, async ValueTask<Unit> (TKey k, object? _, CancellationToken token) => { await taskFunc(k, token).ConfigureAwait(true); return default(Unit); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed Task operation.</summary>
    public TaskSchedulerSubmission Submit<TKey>(ITaskScheduler scheduler, TKey key, Func<TKey, CancellationToken, Task> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => Submit<TKey>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed Task result-producing operation.</summary>
    public TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(ITaskScheduler scheduler, TKey key, Func<TKey, Task<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return Submit<TKey, object?, TResult>(scheduler, key, (TKey k, object? _, CancellationToken token) => { return new ValueTask<TResult>(taskFunc(k)); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed Task result-producing operation.</summary>
    public TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(ITaskScheduler scheduler, TKey key, Func<TKey, Task<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => Submit<TKey, TResult>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed Task result-producing operation.</summary>
    public TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(ITaskScheduler scheduler, TKey key, Func<TKey, CancellationToken, Task<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return Submit<TKey, object?, TResult>(scheduler, key, (TKey k, object? _, CancellationToken token) => { return new ValueTask<TResult>(taskFunc(k, token)); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed Task result-producing operation.</summary>
    public TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(ITaskScheduler scheduler, TKey key, Func<TKey, CancellationToken, Task<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => Submit<TKey, TResult>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed ValueTask operation.</summary>
    public TaskSchedulerSubmission Submit<TKey>(ITaskScheduler scheduler, TKey key, Func<TKey, ValueTask> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return Submit<TKey, object?, Unit>(scheduler, key, async ValueTask<Unit> (TKey k, object? _, CancellationToken token) => { await taskFunc(k).ConfigureAwait(true); return default(Unit); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed ValueTask operation.</summary>
    public TaskSchedulerSubmission Submit<TKey>(ITaskScheduler scheduler, TKey key, Func<TKey, ValueTask> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => Submit<TKey>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed ValueTask operation.</summary>
    public TaskSchedulerSubmission Submit<TKey>(ITaskScheduler scheduler, TKey key, Func<TKey, CancellationToken, ValueTask> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return Submit<TKey, object?, Unit>(scheduler, key, async ValueTask<Unit> (TKey k, object? _, CancellationToken token) => { await taskFunc(k, token).ConfigureAwait(true); return default(Unit); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed ValueTask operation.</summary>
    public TaskSchedulerSubmission Submit<TKey>(ITaskScheduler scheduler, TKey key, Func<TKey, CancellationToken, ValueTask> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => Submit<TKey>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed ValueTask result-producing operation.</summary>
    public TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(ITaskScheduler scheduler, TKey key, Func<TKey, ValueTask<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return Submit<TKey, object?, TResult>(scheduler, key, (TKey k, object? _, CancellationToken token) => { return taskFunc(k); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed ValueTask result-producing operation.</summary>
    public TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(ITaskScheduler scheduler, TKey key, Func<TKey, ValueTask<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => Submit<TKey, TResult>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed ValueTask result-producing operation.</summary>
    public TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(ITaskScheduler scheduler, TKey key, Func<TKey, CancellationToken, ValueTask<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return Submit<TKey, object?, TResult>(scheduler, key, (TKey k, object? _, CancellationToken token) => { return taskFunc(k, token); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed ValueTask result-producing operation.</summary>
    public TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(ITaskScheduler scheduler, TKey key, Func<TKey, CancellationToken, ValueTask<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => Submit<TKey, TResult>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);
}