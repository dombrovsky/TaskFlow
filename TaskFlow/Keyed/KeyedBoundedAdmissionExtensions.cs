namespace System.Threading.Tasks.Flow;

using System.Threading.Tasks.Flow.Annotations;
using System.Threading.Tasks.Flow.Internal;

/// <summary>Provides keyed submission convenience methods without coupling the admission controller to key types.</summary>
public static class KeyedBoundedAdmissionExtensions
{
    /// <summary>Submits through a keyed pipeline, returning the combined admission decision separately from completion.</summary>
    public static TaskSchedulerSubmission<TResult> Submit<TKey, TState, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key,
        Func<TKey, TState, CancellationToken, ValueTask<TResult>> taskFunc, TState state,
        TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        Argument.NotNull(admission);
        BoundedAdmissionController.ValidateMode(admissionMode);
        var pipeline = KeyedTaskSchedulerExtensions.Validate(scheduler, key);
        if (pipeline.CountController(admission) != 1)
            throw new ArgumentException("The pipeline must contain exactly one registration of this controller.", nameof(scheduler));
        var request = new AdmissionRequest(admission, admissionMode);
        var completion = pipeline.SubmitKeyed(key, (s, token) => taskFunc(key, (TState)s!, token), state, request, cancellationToken);
        return new TaskSchedulerSubmission<TResult>(request.Decision, completion);
    }

    /// <summary>Submits through a keyed pipeline, returning the combined admission decision separately from completion.</summary>
    public static TaskSchedulerSubmission<TResult> Submit<TKey, TState, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key,
        Func<TKey, TState, CancellationToken, ValueTask<TResult>> taskFunc, TState state,
        TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => admission.Submit<TKey, TState, TResult>(scheduler, key, taskFunc, state, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed Task operation with typed state.</summary>
    public static TaskSchedulerSubmission<TResult> Submit<TKey, TState, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key,
        Func<TKey, TState, CancellationToken, Task<TResult>> taskFunc, TState state,
        TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.Submit(scheduler, key, (TKey k, TState s, CancellationToken token) => new ValueTask<TResult>(taskFunc(k, s, token)), state, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed Task operation with typed state.</summary>
    public static TaskSchedulerSubmission<TResult> Submit<TKey, TState, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key,
        Func<TKey, TState, CancellationToken, Task<TResult>> taskFunc, TState state,
        TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => admission.Submit<TKey, TState, TResult>(scheduler, key, taskFunc, state, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed Action operation.</summary>
    public static TaskSchedulerSubmission Submit<TKey>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Action taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.Submit<TKey, object?, Unit>(scheduler, key, (TKey k, object? _, CancellationToken token) => { taskFunc(); return new ValueTask<Unit>(default(Unit)); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed Action operation.</summary>
    public static TaskSchedulerSubmission Submit<TKey>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Action taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => admission.Submit<TKey>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed synchronous operation.</summary>
    public static TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<TResult> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.Submit<TKey, object?, TResult>(scheduler, key, (TKey k, object? _, CancellationToken token) => { return new ValueTask<TResult>(taskFunc()); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed synchronous operation.</summary>
    public static TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<TResult> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => admission.Submit<TKey, TResult>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed Task operation.</summary>
    public static TaskSchedulerSubmission Submit<TKey>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<Task> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.Submit<TKey, object?, Unit>(scheduler, key, async ValueTask<Unit> (TKey k, object? _, CancellationToken token) => { await taskFunc().ConfigureAwait(true); return default(Unit); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed Task operation.</summary>
    public static TaskSchedulerSubmission Submit<TKey>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<Task> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => admission.Submit<TKey>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed Task operation.</summary>
    public static TaskSchedulerSubmission Submit<TKey>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<CancellationToken, Task> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.Submit<TKey, object?, Unit>(scheduler, key, async ValueTask<Unit> (TKey k, object? _, CancellationToken token) => { await taskFunc(token).ConfigureAwait(true); return default(Unit); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed Task operation.</summary>
    public static TaskSchedulerSubmission Submit<TKey>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<CancellationToken, Task> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => admission.Submit<TKey>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed Task result-producing operation.</summary>
    public static TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<Task<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.Submit<TKey, object?, TResult>(scheduler, key, (TKey k, object? _, CancellationToken token) => { return new ValueTask<TResult>(taskFunc()); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed Task result-producing operation.</summary>
    public static TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<Task<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => admission.Submit<TKey, TResult>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed Task result-producing operation.</summary>
    public static TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<CancellationToken, Task<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.Submit<TKey, object?, TResult>(scheduler, key, (TKey k, object? _, CancellationToken token) => { return new ValueTask<TResult>(taskFunc(token)); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed Task result-producing operation.</summary>
    public static TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<CancellationToken, Task<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => admission.Submit<TKey, TResult>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed ValueTask operation.</summary>
    public static TaskSchedulerSubmission Submit<TKey>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<ValueTask> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.Submit<TKey, object?, Unit>(scheduler, key, async ValueTask<Unit> (TKey k, object? _, CancellationToken token) => { await taskFunc().ConfigureAwait(true); return default(Unit); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed ValueTask operation.</summary>
    public static TaskSchedulerSubmission Submit<TKey>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<ValueTask> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => admission.Submit<TKey>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed ValueTask operation.</summary>
    public static TaskSchedulerSubmission Submit<TKey>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<CancellationToken, ValueTask> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.Submit<TKey, object?, Unit>(scheduler, key, async ValueTask<Unit> (TKey k, object? _, CancellationToken token) => { await taskFunc(token).ConfigureAwait(true); return default(Unit); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed ValueTask operation.</summary>
    public static TaskSchedulerSubmission Submit<TKey>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<CancellationToken, ValueTask> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => admission.Submit<TKey>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed ValueTask result-producing operation.</summary>
    public static TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<ValueTask<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.Submit<TKey, object?, TResult>(scheduler, key, (TKey k, object? _, CancellationToken token) => { return taskFunc(); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed ValueTask result-producing operation.</summary>
    public static TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<ValueTask<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => admission.Submit<TKey, TResult>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed ValueTask result-producing operation.</summary>
    public static TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<CancellationToken, ValueTask<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.Submit<TKey, object?, TResult>(scheduler, key, (TKey k, object? _, CancellationToken token) => { return taskFunc(token); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed ValueTask result-producing operation.</summary>
    public static TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<CancellationToken, ValueTask<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => admission.Submit<TKey, TResult>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed Action operation.</summary>
    public static TaskSchedulerSubmission Submit<TKey>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Action<TKey> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.Submit<TKey, object?, Unit>(scheduler, key, (TKey k, object? _, CancellationToken token) => { taskFunc(k); return new ValueTask<Unit>(default(Unit)); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed Action operation.</summary>
    public static TaskSchedulerSubmission Submit<TKey>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Action<TKey> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => admission.Submit<TKey>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed synchronous operation.</summary>
    public static TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<TKey, TResult> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.Submit<TKey, object?, TResult>(scheduler, key, (TKey k, object? _, CancellationToken token) => { return new ValueTask<TResult>(taskFunc(k)); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed synchronous operation.</summary>
    public static TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<TKey, TResult> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => admission.Submit<TKey, TResult>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed Task operation.</summary>
    public static TaskSchedulerSubmission Submit<TKey>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<TKey, Task> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.Submit<TKey, object?, Unit>(scheduler, key, async ValueTask<Unit> (TKey k, object? _, CancellationToken token) => { await taskFunc(k).ConfigureAwait(true); return default(Unit); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed Task operation.</summary>
    public static TaskSchedulerSubmission Submit<TKey>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<TKey, Task> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => admission.Submit<TKey>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed Task operation.</summary>
    public static TaskSchedulerSubmission Submit<TKey>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<TKey, CancellationToken, Task> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.Submit<TKey, object?, Unit>(scheduler, key, async ValueTask<Unit> (TKey k, object? _, CancellationToken token) => { await taskFunc(k, token).ConfigureAwait(true); return default(Unit); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed Task operation.</summary>
    public static TaskSchedulerSubmission Submit<TKey>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<TKey, CancellationToken, Task> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => admission.Submit<TKey>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed Task result-producing operation.</summary>
    public static TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<TKey, Task<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.Submit<TKey, object?, TResult>(scheduler, key, (TKey k, object? _, CancellationToken token) => { return new ValueTask<TResult>(taskFunc(k)); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed Task result-producing operation.</summary>
    public static TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<TKey, Task<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => admission.Submit<TKey, TResult>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed Task result-producing operation.</summary>
    public static TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<TKey, CancellationToken, Task<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.Submit<TKey, object?, TResult>(scheduler, key, (TKey k, object? _, CancellationToken token) => { return new ValueTask<TResult>(taskFunc(k, token)); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed Task result-producing operation.</summary>
    public static TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<TKey, CancellationToken, Task<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => admission.Submit<TKey, TResult>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed ValueTask operation.</summary>
    public static TaskSchedulerSubmission Submit<TKey>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<TKey, ValueTask> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.Submit<TKey, object?, Unit>(scheduler, key, async ValueTask<Unit> (TKey k, object? _, CancellationToken token) => { await taskFunc(k).ConfigureAwait(true); return default(Unit); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed ValueTask operation.</summary>
    public static TaskSchedulerSubmission Submit<TKey>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<TKey, ValueTask> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => admission.Submit<TKey>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed ValueTask operation.</summary>
    public static TaskSchedulerSubmission Submit<TKey>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<TKey, CancellationToken, ValueTask> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.Submit<TKey, object?, Unit>(scheduler, key, async ValueTask<Unit> (TKey k, object? _, CancellationToken token) => { await taskFunc(k, token).ConfigureAwait(true); return default(Unit); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed ValueTask operation.</summary>
    public static TaskSchedulerSubmission Submit<TKey>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<TKey, CancellationToken, ValueTask> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => admission.Submit<TKey>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed ValueTask result-producing operation.</summary>
    public static TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<TKey, ValueTask<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.Submit<TKey, object?, TResult>(scheduler, key, (TKey k, object? _, CancellationToken token) => { return taskFunc(k); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed ValueTask result-producing operation.</summary>
    public static TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<TKey, ValueTask<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => admission.Submit<TKey, TResult>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a keyed ValueTask result-producing operation.</summary>
    public static TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<TKey, CancellationToken, ValueTask<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.Submit<TKey, object?, TResult>(scheduler, key, (TKey k, object? _, CancellationToken token) => { return taskFunc(k, token); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a keyed ValueTask result-producing operation.</summary>
    public static TaskSchedulerSubmission<TResult> Submit<TKey, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key, Func<TKey, CancellationToken, ValueTask<TResult>> taskFunc, TaskSchedulerAdmissionMode admissionMode) where TKey : notnull
        => admission.Submit<TKey, TResult>(scheduler, key, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Atomically reserves all coordinated global and per-key budgets, or rejects immediately without admission waiters.</summary>
    /// <remarks>
    /// Supports the same middleware as unkeyed TrySubmit: built-in admission/coordinated enqueue policies, annotations,
    /// and execution/completion middleware. Other enqueue middleware is rejected before submission. Dispatch is asynchronous.
    /// Accepted Completion must be observed; rejection returns Full or Closed with null Completion.
    /// The controller must occur exactly once. The key remains typed and the original state is visible to middleware.
    /// Dispatch requests share a drain and restore each producer's captured ExecutionContext, including flow suppression.
    /// Thread-affine terminals retain their execution thread's ambient context instead of the producer's context.
    /// C# 13+ prefers Task overloads for ordinary async lambdas; older compilers require an explicit delegate type.
    /// Admission uses existing locks and invokes key hashing/equality synchronously; comparers must be fast and nonthrowing.
    /// </remarks>
    /// <exception cref="NotSupportedException">The pipeline cannot guarantee synchronous atomic admission.</exception>
    /// <exception cref="ArgumentException">The controller is missing or registered more than once.</exception>
    /// <exception cref="OperationCanceledException">The caller canceled before admission.</exception>
    public static TaskSchedulerTrySubmission<TResult> TrySubmit<TKey, TState, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key,
        Func<TKey, TState, CancellationToken, ValueTask<TResult>> taskFunc, TState state, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        var pipeline = BoundedAdmissionTaskSchedulerExtensions.ValidateTrySubmit(admission, scheduler);
        pipeline.ValidateKey(key);
        cancellationToken.ThrowIfCancellationRequested();
        var request = new AdmissionRequest(admission, TaskSchedulerAdmissionMode.Reject, synchronous: true);
        var completion = pipeline.SubmitKeyed(key, (s, token) => taskFunc(key, (TState)s!, token), state, request, cancellationToken);
        return PipelineTaskScheduler.FinishTrySubmit(request, completion);
    }

    /// <summary>Immediately attempts combined admission of a keyed ValueTask operation with typed state.</summary>
    public static TaskSchedulerTrySubmission TrySubmit<TKey, TState>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key,
        Func<TKey, TState, CancellationToken, ValueTask> taskFunc, TState state, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.TrySubmit<TKey, TState, Unit>(scheduler, key, async (k, s, token) => { await taskFunc(k, s, token).ConfigureAwait(true); return default; }, state, cancellationToken);
    }

    /// <summary>Immediately attempts combined admission of a keyed synchronous operation.</summary>
    public static TaskSchedulerTrySubmission TrySubmit<TKey>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key,
        Action<TKey> taskFunc, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.TrySubmit<TKey, object?, Unit>(scheduler, key, (k, _, _) => { taskFunc(k); return new ValueTask<Unit>(default(Unit)); }, null, cancellationToken);
    }

    /// <summary>Immediately attempts combined admission of a keyed synchronous result-producing operation.</summary>
    public static TaskSchedulerTrySubmission<TResult> TrySubmit<TKey, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key,
        Func<TKey, TResult> taskFunc, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.TrySubmit<TKey, object?, TResult>(scheduler, key, (k, _, _) => new ValueTask<TResult>(taskFunc(k)), null, cancellationToken);
    }

    /// <summary>Immediately attempts combined keyed admission of a Task operation.</summary>
    [System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
    public static TaskSchedulerTrySubmission TrySubmit<TKey>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key,
        Func<TKey, CancellationToken, Task> taskFunc, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.TrySubmit<TKey, object?, Unit>(scheduler, key, async (k, _, token) => { await taskFunc(k, token).ConfigureAwait(true); return default; }, null, cancellationToken);
    }

    /// <summary>Immediately attempts combined keyed admission of a Task result-producing operation.</summary>
    [System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
    public static TaskSchedulerTrySubmission<TResult> TrySubmit<TKey, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key,
        Func<TKey, CancellationToken, Task<TResult>> taskFunc, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.TrySubmit<TKey, object?, TResult>(scheduler, key, (k, _, token) => new ValueTask<TResult>(taskFunc(k, token)), null, cancellationToken);
    }

    /// <summary>Immediately attempts combined keyed admission of a ValueTask operation.</summary>
    public static TaskSchedulerTrySubmission TrySubmit<TKey>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key,
        Func<TKey, CancellationToken, ValueTask> taskFunc, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.TrySubmit<TKey, object?, Unit>(scheduler, key, async (k, _, token) => { await taskFunc(k, token).ConfigureAwait(true); return default; }, null, cancellationToken);
    }

    /// <summary>Immediately attempts combined keyed admission of a ValueTask result-producing operation.</summary>
    public static TaskSchedulerTrySubmission<TResult> TrySubmit<TKey, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key,
        Func<TKey, CancellationToken, ValueTask<TResult>> taskFunc, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.TrySubmit<TKey, object?, TResult>(scheduler, key, (k, _, token) => taskFunc(k, token), null, cancellationToken);
    }

    /// <summary>Immediately attempts admission without caller cancellation.</summary>
    public static TaskSchedulerTrySubmission<TResult> TrySubmit<TKey, TState, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key,
        Func<TKey, TState, CancellationToken, ValueTask<TResult>> taskFunc, TState state) where TKey : notnull
        => admission.TrySubmit<TKey, TState, TResult>(scheduler, key, taskFunc, state, CancellationToken.None);

    /// <summary>Immediately attempts admission without caller cancellation.</summary>
    public static TaskSchedulerTrySubmission TrySubmit<TKey, TState>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key,
        Func<TKey, TState, CancellationToken, ValueTask> taskFunc, TState state) where TKey : notnull
        => admission.TrySubmit<TKey, TState>(scheduler, key, taskFunc, state, CancellationToken.None);

    /// <summary>Immediately attempts admission without caller cancellation.</summary>
    public static TaskSchedulerTrySubmission TrySubmit<TKey>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key,
        Action<TKey> taskFunc) where TKey : notnull
        => admission.TrySubmit<TKey>(scheduler, key, taskFunc, CancellationToken.None);

    /// <summary>Immediately attempts admission without caller cancellation.</summary>
    public static TaskSchedulerTrySubmission<TResult> TrySubmit<TKey, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key,
        Func<TKey, TResult> taskFunc) where TKey : notnull
        => admission.TrySubmit<TKey, TResult>(scheduler, key, taskFunc, CancellationToken.None);

    /// <summary>Immediately attempts admission without caller cancellation.</summary>
    [System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
    public static TaskSchedulerTrySubmission TrySubmit<TKey>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key,
        Func<TKey, CancellationToken, Task> taskFunc) where TKey : notnull
        => admission.TrySubmit<TKey>(scheduler, key, taskFunc, CancellationToken.None);

    /// <summary>Immediately attempts admission without caller cancellation.</summary>
    [System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
    public static TaskSchedulerTrySubmission<TResult> TrySubmit<TKey, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key,
        Func<TKey, CancellationToken, Task<TResult>> taskFunc) where TKey : notnull
        => admission.TrySubmit<TKey, TResult>(scheduler, key, taskFunc, CancellationToken.None);

    /// <summary>Immediately attempts admission without caller cancellation.</summary>
    public static TaskSchedulerTrySubmission TrySubmit<TKey>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key,
        Func<TKey, CancellationToken, ValueTask> taskFunc) where TKey : notnull
        => admission.TrySubmit<TKey>(scheduler, key, taskFunc, CancellationToken.None);

    /// <summary>Immediately attempts admission without caller cancellation.</summary>
    public static TaskSchedulerTrySubmission<TResult> TrySubmit<TKey, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key,
        Func<TKey, CancellationToken, ValueTask<TResult>> taskFunc) where TKey : notnull
        => admission.TrySubmit<TKey, TResult>(scheduler, key, taskFunc, CancellationToken.None);

    /// <summary>Immediately attempts combined keyed admission of a Task operation without a delegate token parameter.</summary>
    [System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
    public static TaskSchedulerTrySubmission TrySubmit<TKey>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key,
        Func<TKey, Task> taskFunc, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.TrySubmit<TKey, object?, Unit>(scheduler, key, async (k, _, _) => { await taskFunc(k).ConfigureAwait(true); return default; }, null, cancellationToken);
    }

    /// <summary>Immediately attempts admission without caller cancellation.</summary>
    [System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
    public static TaskSchedulerTrySubmission TrySubmit<TKey>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key,
        Func<TKey, Task> taskFunc) where TKey : notnull
        => admission.TrySubmit<TKey>(scheduler, key, taskFunc, CancellationToken.None);

    /// <summary>Immediately attempts combined keyed admission of a Task result-producing operation without a delegate token parameter.</summary>
    [System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
    public static TaskSchedulerTrySubmission<TResult> TrySubmit<TKey, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key,
        Func<TKey, Task<TResult>> taskFunc, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.TrySubmit<TKey, object?, TResult>(scheduler, key, (k, _, _) => new ValueTask<TResult>(taskFunc(k)), null, cancellationToken);
    }

    /// <summary>Immediately attempts admission without caller cancellation.</summary>
    [System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
    public static TaskSchedulerTrySubmission<TResult> TrySubmit<TKey, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key,
        Func<TKey, Task<TResult>> taskFunc) where TKey : notnull
        => admission.TrySubmit<TKey, TResult>(scheduler, key, taskFunc, CancellationToken.None);

    /// <summary>Immediately attempts combined keyed admission of a ValueTask operation without a delegate token parameter.</summary>
    public static TaskSchedulerTrySubmission TrySubmit<TKey>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key,
        Func<TKey, ValueTask> taskFunc, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.TrySubmit<TKey, object?, Unit>(scheduler, key, async (k, _, _) => { await taskFunc(k).ConfigureAwait(true); return default; }, null, cancellationToken);
    }

    /// <summary>Immediately attempts admission without caller cancellation.</summary>
    public static TaskSchedulerTrySubmission TrySubmit<TKey>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key,
        Func<TKey, ValueTask> taskFunc) where TKey : notnull
        => admission.TrySubmit<TKey>(scheduler, key, taskFunc, CancellationToken.None);

    /// <summary>Immediately attempts combined keyed admission of a ValueTask result-producing operation without a delegate token parameter.</summary>
    public static TaskSchedulerTrySubmission<TResult> TrySubmit<TKey, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key,
        Func<TKey, ValueTask<TResult>> taskFunc, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return admission.TrySubmit<TKey, object?, TResult>(scheduler, key, (k, _, _) => taskFunc(k), null, cancellationToken);
    }

    /// <summary>Immediately attempts admission without caller cancellation.</summary>
    public static TaskSchedulerTrySubmission<TResult> TrySubmit<TKey, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler, TKey key,
        Func<TKey, ValueTask<TResult>> taskFunc) where TKey : notnull
        => admission.TrySubmit<TKey, TResult>(scheduler, key, taskFunc, CancellationToken.None);

}