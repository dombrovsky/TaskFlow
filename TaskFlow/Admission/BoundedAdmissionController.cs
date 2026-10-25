namespace System.Threading.Tasks.Flow;

using System.Threading.Tasks.Flow.Annotations;
using System.Threading.Tasks.Flow.Internal;

/// <summary>Defines bounded admission and identifies the registration targeted by explicit submissions.</summary>
/// <remarks>Each registration has independent capacity. Descendant pipeline snapshots share an existing registration.</remarks>
public sealed class BoundedAdmissionController : ITaskSchedulerMiddlewareFactory
{
    internal int Capacity { get; }
    internal int? MaxAdmissionWaiters { get; }
    internal TaskSchedulerCapacityMode CapacityMode { get; }
    internal TaskSchedulerAdmissionMode AdmissionMode { get; }

    /// <summary>Captures and validates bounded admission settings.</summary>
    public BoundedAdmissionController(BoundedAdmissionOptions options)
    {
        Argument.NotNull(options);
        if (options.Capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Capacity must be positive.");
        if (options.MaxAdmissionWaiters < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "The waiter limit cannot be negative.");
        if (options.CapacityMode < TaskSchedulerCapacityMode.Outstanding || options.CapacityMode > TaskSchedulerCapacityMode.Pending)
            throw new ArgumentOutOfRangeException(nameof(options), "Unknown capacity mode.");
        ValidateMode(options.AdmissionMode);
        Capacity = options.Capacity;
        MaxAdmissionWaiters = options.MaxAdmissionWaiters;
        CapacityMode = options.CapacityMode;
        AdmissionMode = options.AdmissionMode;
    }

    ITaskSchedulerMiddleware ITaskSchedulerMiddlewareFactory.Create(TaskSchedulerMiddlewareLifetime lifetime)
        => new BoundedAdmissionMiddleware(this, lifetime);

    /// <summary>Submits through a native pipeline, overriding this controller's registration only.</summary>
    /// <remarks>Reject never waits for capacity; upstream middleware can still delay the admission decision. Use TrySubmit extensions for a synchronous decision on supported pipelines. The controller must occur exactly once in the pipeline.</remarks>
    public TaskSchedulerSubmission<T> Submit<T>(ITaskScheduler scheduler, Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken)
    {
        Argument.NotNull(scheduler);
        Argument.NotNull(taskFunc);
        ValidateMode(admissionMode);
        if (!(scheduler is PipelineTaskScheduler pipeline) || pipeline.CountController(this) != 1)
            throw new ArgumentException("The scheduler must be a native pipeline containing exactly one registration of this controller.", nameof(scheduler));
        var request = new AdmissionRequest(this, admissionMode);
        var completion = pipeline.Submit(taskFunc, state, request, cancellationToken);
        return new TaskSchedulerSubmission<T>(request.Decision, completion);
    }

    /// <summary>Submits a synchronous result-producing operation with an explicit admission mode.</summary>
    public TaskSchedulerSubmission<T> Submit<T>(ITaskScheduler scheduler, Func<T> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken)
    {
        Argument.NotNull(taskFunc);
        return Submit(scheduler, (_, _) => new ValueTask<T>(taskFunc()), null, admissionMode, cancellationToken);
    }

    /// <summary>Submits an asynchronous result-producing operation with an explicit admission mode.</summary>
    public TaskSchedulerSubmission<T> Submit<T>(ITaskScheduler scheduler, Func<CancellationToken, Task<T>> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken)
    {
        Argument.NotNull(taskFunc);
        return Submit(scheduler, (_, token) => new ValueTask<T>(taskFunc(token)), null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a synchronous operation with an explicit admission mode.</summary>
    public TaskSchedulerSubmission Submit(ITaskScheduler scheduler, Action taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken)
    {
        Argument.NotNull(taskFunc);
        return Submit(scheduler, (_, _) => { taskFunc(); return new ValueTask<Unit>(default(Unit)); }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits an asynchronous operation with an explicit admission mode.</summary>
    public TaskSchedulerSubmission Submit(ITaskScheduler scheduler, Func<CancellationToken, Task> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken)
    {
        Argument.NotNull(taskFunc);
        return Submit<Unit>(scheduler, async (_, token) => { await taskFunc(token).ConfigureAwait(true); return default; }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a synchronous result-producing operation without caller cancellation.</summary>
    public TaskSchedulerSubmission<T> Submit<T>(ITaskScheduler scheduler, Func<T> taskFunc, TaskSchedulerAdmissionMode admissionMode)
        => Submit(scheduler, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits an asynchronous result-producing operation without caller cancellation.</summary>
    public TaskSchedulerSubmission<T> Submit<T>(ITaskScheduler scheduler, Func<CancellationToken, Task<T>> taskFunc, TaskSchedulerAdmissionMode admissionMode)
        => Submit(scheduler, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a synchronous operation without caller cancellation.</summary>
    public TaskSchedulerSubmission Submit(ITaskScheduler scheduler, Action taskFunc, TaskSchedulerAdmissionMode admissionMode)
        => Submit(scheduler, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits an asynchronous operation without caller cancellation.</summary>
    public TaskSchedulerSubmission Submit(ITaskScheduler scheduler, Func<CancellationToken, Task> taskFunc, TaskSchedulerAdmissionMode admissionMode)
        => Submit(scheduler, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits an asynchronous result-producing operation without a delegate token parameter.</summary>
    public TaskSchedulerSubmission<T> Submit<T>(ITaskScheduler scheduler, Func<Task<T>> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken)
    {
        Argument.NotNull(taskFunc);
        return Submit(scheduler, (_, _) => new ValueTask<T>(taskFunc()), null, admissionMode, cancellationToken);
    }

    /// <summary>Submits an asynchronous result-producing operation without caller cancellation.</summary>
    public TaskSchedulerSubmission<T> Submit<T>(ITaskScheduler scheduler, Func<Task<T>> taskFunc, TaskSchedulerAdmissionMode admissionMode)
        => Submit(scheduler, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits an asynchronous operation without a delegate token parameter.</summary>
    public TaskSchedulerSubmission Submit(ITaskScheduler scheduler, Func<Task> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken)
    {
        Argument.NotNull(taskFunc);
        return Submit<Unit>(scheduler, async (_, _) => { await taskFunc().ConfigureAwait(true); return default; }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits an asynchronous operation without caller cancellation.</summary>
    public TaskSchedulerSubmission Submit(ITaskScheduler scheduler, Func<Task> taskFunc, TaskSchedulerAdmissionMode admissionMode)
        => Submit(scheduler, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a ValueTask operation with an explicit admission mode.</summary>
    public TaskSchedulerSubmission<T> Submit<T>(ITaskScheduler scheduler, Func<CancellationToken, ValueTask<T>> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken)
    {
        Argument.NotNull(taskFunc);
        return Submit(scheduler, (_, token) => taskFunc(token), null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a ValueTask operation without caller cancellation.</summary>
    public TaskSchedulerSubmission<T> Submit<T>(ITaskScheduler scheduler, Func<CancellationToken, ValueTask<T>> taskFunc, TaskSchedulerAdmissionMode admissionMode)
        => Submit(scheduler, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a ValueTask operation with an explicit admission mode.</summary>
    public TaskSchedulerSubmission<T> Submit<T>(ITaskScheduler scheduler, Func<ValueTask<T>> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken)
    {
        Argument.NotNull(taskFunc);
        return Submit(scheduler, (_, token) => taskFunc(), null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a ValueTask operation without caller cancellation.</summary>
    public TaskSchedulerSubmission<T> Submit<T>(ITaskScheduler scheduler, Func<ValueTask<T>> taskFunc, TaskSchedulerAdmissionMode admissionMode)
        => Submit(scheduler, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a ValueTask operation with an explicit admission mode.</summary>
    public TaskSchedulerSubmission Submit(ITaskScheduler scheduler, Func<CancellationToken, ValueTask> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken)
    {
        Argument.NotNull(taskFunc);
        return Submit<Unit>(scheduler, async (_, token) => { await taskFunc(token).ConfigureAwait(true); return default; }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a ValueTask operation without caller cancellation.</summary>
    public TaskSchedulerSubmission Submit(ITaskScheduler scheduler, Func<CancellationToken, ValueTask> taskFunc, TaskSchedulerAdmissionMode admissionMode)
        => Submit(scheduler, taskFunc, admissionMode, CancellationToken.None);

    /// <summary>Submits a ValueTask operation with an explicit admission mode.</summary>
    public TaskSchedulerSubmission Submit(ITaskScheduler scheduler, Func<ValueTask> taskFunc, TaskSchedulerAdmissionMode admissionMode, CancellationToken cancellationToken)
    {
        Argument.NotNull(taskFunc);
        return Submit<Unit>(scheduler, async (_, token) => { await taskFunc().ConfigureAwait(true); return default; }, null, admissionMode, cancellationToken);
    }

    /// <summary>Submits a ValueTask operation without caller cancellation.</summary>
    public TaskSchedulerSubmission Submit(ITaskScheduler scheduler, Func<ValueTask> taskFunc, TaskSchedulerAdmissionMode admissionMode)
        => Submit(scheduler, taskFunc, admissionMode, CancellationToken.None);

    internal static void ValidateMode(TaskSchedulerAdmissionMode mode)
    {
        if (mode < TaskSchedulerAdmissionMode.Wait || mode > TaskSchedulerAdmissionMode.Reject)
            throw new ArgumentOutOfRangeException(nameof(mode));
    }
}