namespace System.Threading.Tasks.Flow;

using System.Threading.Tasks.Flow.Annotations;

/// <summary>Configures registration-local bounded admission and submits operations with an immediate decision.</summary>
public static class BoundedAdmissionTaskSchedulerExtensions
{
    /// <summary>Creates a bounded registration with captured options.</summary>
    public static ITaskScheduler WithBoundedAdmission(this ITaskScheduler scheduler, BoundedAdmissionOptions options)
        => scheduler.WithBoundedAdmission(new BoundedAdmissionController(options));

    /// <summary>Creates a bounded registration identified by a controller.</summary>
    public static ITaskScheduler WithBoundedAdmission(this ITaskScheduler scheduler, BoundedAdmissionController admission)
    {
        Argument.NotNull(scheduler);
        Argument.NotNull(admission);
        if (scheduler is Internal.PipelineTaskScheduler pipeline && pipeline.Coordinator != null)
            return pipeline.Append(new Internal.AdmissionPolicy(admission, perKey: false));
        return scheduler.UseMiddleware(admission);
    }

    /// <summary>Creates a bounded registration with an explicitly owned lifetime.</summary>
    public static ITaskScheduler WithBoundedAdmission(this ITaskScheduler scheduler, BoundedAdmissionController admission, TaskSchedulerMiddlewareLifetime lifetime)
    {
        Argument.NotNull(scheduler);
        Argument.NotNull(admission);
        return scheduler.UseMiddleware(admission, lifetime);
    }

    /// <summary>Atomically admits an operation now, without capacity waiting or admission waiters.</summary>
    /// <remarks>
    /// The controller must occur exactly once in a native pipeline. Only built-in admission/coordinated enqueue policies,
    /// annotations, and execution/completion middleware are supported. Multiple budgets require a coordinated branch.
    /// Dispatch runs off the producer's stack, including terminal scheduler callbacks; accepted work can begin before return.
    /// Dispatch restores the producer's captured ExecutionContext, including flow suppression. Thread-affine terminals
    /// retain their execution thread's ambient context. Existing admission/lifetime locks may contend.
    /// Observe accepted Completion tasks. Full/Closed rejections have null Completion. Pre-admission cancellation throws.
    /// </remarks>
    /// <exception cref="NotSupportedException">The pipeline cannot guarantee synchronous atomic admission. Validation occurs before work is submitted.</exception>
    /// <exception cref="ArgumentException">The controller is missing or registered more than once.</exception>
    /// <exception cref="OperationCanceledException">The caller canceled before admission.</exception>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "These new APIs differ by delegate shape; optional cancellation avoids token/no-token duplication. Compile tests cover overload selection.")]
    public static TaskSchedulerTrySubmission<TResult> TrySubmit<TState, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler,
        Func<TState, CancellationToken, ValueTask<TResult>> taskFunc, TState state, CancellationToken cancellationToken = default)
    {
        Argument.NotNull(taskFunc);
        var pipeline = ValidateTrySubmit(admission, scheduler);
        cancellationToken.ThrowIfCancellationRequested();
        var request = new Internal.AdmissionRequest(admission, TaskSchedulerAdmissionMode.Reject, synchronous: true);
        var completion = pipeline.Submit((s, token) => taskFunc((TState)s!, token), state, request, cancellationToken);
        return FinishTrySubmit(request, completion);
    }

    /// <summary>Immediately attempts admission of a ValueTask operation with typed state. See the result-producing overload for pipeline requirements.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "These new APIs differ by delegate shape; optional cancellation avoids token/no-token duplication. Compile tests cover overload selection.")]
    public static TaskSchedulerTrySubmission TrySubmit<TState>(this BoundedAdmissionController admission, ITaskScheduler scheduler,
        Func<TState, CancellationToken, ValueTask> taskFunc, TState state, CancellationToken cancellationToken = default)
    {
        Argument.NotNull(taskFunc);
        return admission.TrySubmit<TState, Internal.Unit>(scheduler, async (s, token) => { await taskFunc(s, token).ConfigureAwait(true); return default; }, state, cancellationToken);
    }

    /// <summary>Immediately attempts admission of a Task operation.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "These new APIs differ by delegate shape; optional cancellation avoids token/no-token duplication. Compile tests cover overload selection.")]
    public static TaskSchedulerTrySubmission TrySubmit(this BoundedAdmissionController admission, ITaskScheduler scheduler,
        Func<CancellationToken, Task> taskFunc, CancellationToken cancellationToken = default)
    {
        Argument.NotNull(taskFunc);
        return admission.TrySubmit<object?, Internal.Unit>(scheduler, async (_, token) => { await taskFunc(token).ConfigureAwait(true); return default; }, null, cancellationToken);
    }

    /// <summary>Immediately attempts admission of a Task result-producing operation.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "These new APIs differ by delegate shape; optional cancellation avoids token/no-token duplication. Compile tests cover overload selection.")]
    public static TaskSchedulerTrySubmission<TResult> TrySubmit<TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler,
        Func<CancellationToken, Task<TResult>> taskFunc, CancellationToken cancellationToken = default)
    {
        Argument.NotNull(taskFunc);
        return admission.TrySubmit<object?, TResult>(scheduler, (_, token) => new ValueTask<TResult>(taskFunc(token)), null, cancellationToken);
    }

    /// <summary>Immediately attempts admission of a Task operation without a delegate token parameter.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "These new APIs differ by delegate shape; optional cancellation avoids token/no-token duplication. Compile tests cover overload selection.")]
    public static TaskSchedulerTrySubmission TrySubmit(this BoundedAdmissionController admission, ITaskScheduler scheduler,
        Func<Task> taskFunc, CancellationToken cancellationToken = default)
    {
        Argument.NotNull(taskFunc);
        return admission.TrySubmit<object?, Internal.Unit>(scheduler, async (_, _) => { await taskFunc().ConfigureAwait(true); return default; }, null, cancellationToken);
    }

    /// <summary>Immediately attempts admission of a Task result-producing operation without a delegate token parameter.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "These new APIs differ by delegate shape; optional cancellation avoids token/no-token duplication. Compile tests cover overload selection.")]
    public static TaskSchedulerTrySubmission<TResult> TrySubmit<TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler,
        Func<Task<TResult>> taskFunc, CancellationToken cancellationToken = default)
    {
        Argument.NotNull(taskFunc);
        return admission.TrySubmit<object?, TResult>(scheduler, (_, _) => new ValueTask<TResult>(taskFunc()), null, cancellationToken);
    }

    internal static TaskSchedulerTrySubmission<TResult> FinishTrySubmit<TResult>(Internal.AdmissionRequest request, Task<TResult> completion)
    {
        var decision = request.Decision;
        // Capability validation guarantees a settled decision; never wait on an incomplete task.
        if (!decision.IsCompleted)
        {
            ObserveUnreturned(decision);
            ObserveUnreturned(completion);
            throw new InvalidOperationException("The pipeline did not make a synchronous admission decision.");
        }
        try
        {
            var result = decision.GetAwaiter().GetResult();
            if (result == TaskSchedulerAdmissionResult.Accepted)
                return new TaskSchedulerTrySubmission<TResult>(result, completion);
            ObserveUnreturned(completion);
            return new TaskSchedulerTrySubmission<TResult>(result, null);
        }
        catch
        {
            ObserveUnreturned(completion);
            throw;
        }
    }

    private static void ObserveUnreturned(Task task)
    {
        _ = task.ContinueWith(completed => { _ = completed.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    internal static Internal.PipelineTaskScheduler ValidateTrySubmit(BoundedAdmissionController admission, ITaskScheduler scheduler)
    {
        Argument.NotNull(admission);
        Argument.NotNull(scheduler);
        if (scheduler is not Internal.PipelineTaskScheduler pipeline)
            throw new ArgumentException("The scheduler must be a native pipeline containing this controller.", nameof(scheduler));
        if (pipeline.CountController(admission) != 1)
            throw new ArgumentException("The pipeline must contain exactly one registration of this controller.", nameof(scheduler));
        pipeline.ValidateTrySubmit();
        return pipeline;
    }
}