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
    /// Dispatch is coalesced off the producer's stack, including terminal scheduler callbacks; accepted work can begin
    /// before this method returns. Dispatch restores the producer's captured ExecutionContext, including flow suppression.
    /// Thread-affine terminals retain their execution thread's ambient context instead of the producer's context.
    /// C# 13+ prefers Task overloads for ordinary async lambdas; older compilers require an explicit delegate type.
    /// Existing admission/lifetime locks may contend. Observe accepted Completion tasks.
    /// Rejection returns Full or Closed with null Completion. A cancellation that wins before admission throws synchronously.
    /// </remarks>
    /// <exception cref="NotSupportedException">The pipeline cannot guarantee synchronous atomic admission. Validation occurs before work is submitted.</exception>
    /// <exception cref="ArgumentException">The controller is missing or registered more than once.</exception>
    /// <exception cref="OperationCanceledException">The caller canceled before admission.</exception>
    public static TaskSchedulerTrySubmission<TResult> TrySubmit<TState, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler,
        Func<TState, CancellationToken, ValueTask<TResult>> taskFunc, TState state, CancellationToken cancellationToken)
    {
        Argument.NotNull(taskFunc);
        var pipeline = ValidateTrySubmit(admission, scheduler);
        cancellationToken.ThrowIfCancellationRequested();
        var request = new Internal.AdmissionRequest(admission, TaskSchedulerAdmissionMode.Reject, synchronous: true);
        var completion = pipeline.Submit((s, token) => taskFunc((TState)s!, token), state, request, cancellationToken);
        return Internal.PipelineTaskScheduler.FinishTrySubmit(request, completion);
    }

    /// <summary>Immediately attempts admission of a ValueTask operation with typed state. See the result-producing overload for pipeline requirements.</summary>
    public static TaskSchedulerTrySubmission TrySubmit<TState>(this BoundedAdmissionController admission, ITaskScheduler scheduler,
        Func<TState, CancellationToken, ValueTask> taskFunc, TState state, CancellationToken cancellationToken)
    {
        Argument.NotNull(taskFunc);
        return admission.TrySubmit<TState, Internal.Unit>(scheduler, async (s, token) => { await taskFunc(s, token).ConfigureAwait(true); return default; }, state, cancellationToken);
    }

    /// <summary>Immediately attempts admission of a synchronous operation.</summary>
    public static TaskSchedulerTrySubmission TrySubmit(this BoundedAdmissionController admission, ITaskScheduler scheduler,
        Action taskFunc, CancellationToken cancellationToken)
    {
        Argument.NotNull(taskFunc);
        return admission.TrySubmit<object?, Internal.Unit>(scheduler, (_, _) => { taskFunc(); return new ValueTask<Internal.Unit>(default(Internal.Unit)); }, null, cancellationToken);
    }

    /// <summary>Immediately attempts admission of a synchronous result-producing operation.</summary>
    public static TaskSchedulerTrySubmission<TResult> TrySubmit<TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler,
        Func<TResult> taskFunc, CancellationToken cancellationToken)
    {
        Argument.NotNull(taskFunc);
        return admission.TrySubmit<object?, TResult>(scheduler, (_, _) => new ValueTask<TResult>(taskFunc()), null, cancellationToken);
    }

    /// <summary>Immediately attempts admission of a Task operation.</summary>
    [System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
    public static TaskSchedulerTrySubmission TrySubmit(this BoundedAdmissionController admission, ITaskScheduler scheduler,
        Func<CancellationToken, Task> taskFunc, CancellationToken cancellationToken)
    {
        Argument.NotNull(taskFunc);
        return admission.TrySubmit<object?, Internal.Unit>(scheduler, async (_, token) => { await taskFunc(token).ConfigureAwait(true); return default; }, null, cancellationToken);
    }

    /// <summary>Immediately attempts admission of a Task result-producing operation.</summary>
    [System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
    public static TaskSchedulerTrySubmission<TResult> TrySubmit<TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler,
        Func<CancellationToken, Task<TResult>> taskFunc, CancellationToken cancellationToken)
    {
        Argument.NotNull(taskFunc);
        return admission.TrySubmit<object?, TResult>(scheduler, (_, token) => new ValueTask<TResult>(taskFunc(token)), null, cancellationToken);
    }

    /// <summary>Immediately attempts admission of a ValueTask operation.</summary>
    public static TaskSchedulerTrySubmission TrySubmit(this BoundedAdmissionController admission, ITaskScheduler scheduler,
        Func<CancellationToken, ValueTask> taskFunc, CancellationToken cancellationToken)
    {
        Argument.NotNull(taskFunc);
        return admission.TrySubmit<object?, Internal.Unit>(scheduler, async (_, token) => { await taskFunc(token).ConfigureAwait(true); return default; }, null, cancellationToken);
    }

    /// <summary>Immediately attempts admission of a ValueTask result-producing operation.</summary>
    public static TaskSchedulerTrySubmission<TResult> TrySubmit<TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler,
        Func<CancellationToken, ValueTask<TResult>> taskFunc, CancellationToken cancellationToken)
    {
        Argument.NotNull(taskFunc);
        return admission.TrySubmit<object?, TResult>(scheduler, (_, token) => taskFunc(token), null, cancellationToken);
    }

    /// <summary>Immediately attempts admission without caller cancellation.</summary>
    public static TaskSchedulerTrySubmission<TResult> TrySubmit<TState, TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler,
        Func<TState, CancellationToken, ValueTask<TResult>> taskFunc, TState state)
        => admission.TrySubmit<TState, TResult>(scheduler, taskFunc, state, CancellationToken.None);

    /// <summary>Immediately attempts admission without caller cancellation.</summary>
    public static TaskSchedulerTrySubmission TrySubmit<TState>(this BoundedAdmissionController admission, ITaskScheduler scheduler,
        Func<TState, CancellationToken, ValueTask> taskFunc, TState state)
        => admission.TrySubmit<TState>(scheduler, taskFunc, state, CancellationToken.None);

    /// <summary>Immediately attempts admission without caller cancellation.</summary>
    public static TaskSchedulerTrySubmission TrySubmit(this BoundedAdmissionController admission, ITaskScheduler scheduler,
        Action taskFunc)
        => admission.TrySubmit(scheduler, taskFunc, CancellationToken.None);

    /// <summary>Immediately attempts admission without caller cancellation.</summary>
    public static TaskSchedulerTrySubmission<TResult> TrySubmit<TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler,
        Func<TResult> taskFunc)
        => admission.TrySubmit<TResult>(scheduler, taskFunc, CancellationToken.None);

    /// <summary>Immediately attempts admission without caller cancellation.</summary>
    [System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
    public static TaskSchedulerTrySubmission TrySubmit(this BoundedAdmissionController admission, ITaskScheduler scheduler,
        Func<CancellationToken, Task> taskFunc)
        => admission.TrySubmit(scheduler, taskFunc, CancellationToken.None);

    /// <summary>Immediately attempts admission without caller cancellation.</summary>
    [System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
    public static TaskSchedulerTrySubmission<TResult> TrySubmit<TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler,
        Func<CancellationToken, Task<TResult>> taskFunc)
        => admission.TrySubmit<TResult>(scheduler, taskFunc, CancellationToken.None);

    /// <summary>Immediately attempts admission without caller cancellation.</summary>
    public static TaskSchedulerTrySubmission TrySubmit(this BoundedAdmissionController admission, ITaskScheduler scheduler,
        Func<CancellationToken, ValueTask> taskFunc)
        => admission.TrySubmit(scheduler, taskFunc, CancellationToken.None);

    /// <summary>Immediately attempts admission without caller cancellation.</summary>
    public static TaskSchedulerTrySubmission<TResult> TrySubmit<TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler,
        Func<CancellationToken, ValueTask<TResult>> taskFunc)
        => admission.TrySubmit<TResult>(scheduler, taskFunc, CancellationToken.None);


    /// <summary>Immediately attempts admission of a Task operation without a delegate token parameter.</summary>
    [System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
    public static TaskSchedulerTrySubmission TrySubmit(this BoundedAdmissionController admission, ITaskScheduler scheduler,
        Func<Task> taskFunc, CancellationToken cancellationToken)
    {
        Argument.NotNull(taskFunc);
        return admission.TrySubmit<object?, Internal.Unit>(scheduler, async (_, _) => { await taskFunc().ConfigureAwait(true); return default; }, null, cancellationToken);
    }

    /// <summary>Immediately attempts admission without caller cancellation.</summary>
    [System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
    public static TaskSchedulerTrySubmission TrySubmit(this BoundedAdmissionController admission, ITaskScheduler scheduler,
        Func<Task> taskFunc)
        => admission.TrySubmit(scheduler, taskFunc, CancellationToken.None);

    /// <summary>Immediately attempts admission of a Task result-producing operation without a delegate token parameter.</summary>
    [System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
    public static TaskSchedulerTrySubmission<TResult> TrySubmit<TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler,
        Func<Task<TResult>> taskFunc, CancellationToken cancellationToken)
    {
        Argument.NotNull(taskFunc);
        return admission.TrySubmit<object?, TResult>(scheduler, (_, _) => new ValueTask<TResult>(taskFunc()), null, cancellationToken);
    }

    /// <summary>Immediately attempts admission without caller cancellation.</summary>
    [System.Runtime.CompilerServices.OverloadResolutionPriority(1)]
    public static TaskSchedulerTrySubmission<TResult> TrySubmit<TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler,
        Func<Task<TResult>> taskFunc)
        => admission.TrySubmit<TResult>(scheduler, taskFunc, CancellationToken.None);

    /// <summary>Immediately attempts admission of a ValueTask operation without a delegate token parameter.</summary>
    public static TaskSchedulerTrySubmission TrySubmit(this BoundedAdmissionController admission, ITaskScheduler scheduler,
        Func<ValueTask> taskFunc, CancellationToken cancellationToken)
    {
        Argument.NotNull(taskFunc);
        return admission.TrySubmit<object?, Internal.Unit>(scheduler, async (_, _) => { await taskFunc().ConfigureAwait(true); return default; }, null, cancellationToken);
    }

    /// <summary>Immediately attempts admission without caller cancellation.</summary>
    public static TaskSchedulerTrySubmission TrySubmit(this BoundedAdmissionController admission, ITaskScheduler scheduler,
        Func<ValueTask> taskFunc)
        => admission.TrySubmit(scheduler, taskFunc, CancellationToken.None);

    /// <summary>Immediately attempts admission of a ValueTask result-producing operation without a delegate token parameter.</summary>
    public static TaskSchedulerTrySubmission<TResult> TrySubmit<TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler,
        Func<ValueTask<TResult>> taskFunc, CancellationToken cancellationToken)
    {
        Argument.NotNull(taskFunc);
        return admission.TrySubmit<object?, TResult>(scheduler, (_, _) => taskFunc(), null, cancellationToken);
    }

    /// <summary>Immediately attempts admission without caller cancellation.</summary>
    public static TaskSchedulerTrySubmission<TResult> TrySubmit<TResult>(this BoundedAdmissionController admission, ITaskScheduler scheduler,
        Func<ValueTask<TResult>> taskFunc)
        => admission.TrySubmit<TResult>(scheduler, taskFunc, CancellationToken.None);

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