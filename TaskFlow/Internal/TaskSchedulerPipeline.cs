namespace System.Threading.Tasks.Flow.Internal;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

internal sealed class PipelineTaskScheduler : ITaskScheduler
{
    private readonly ITaskScheduler _terminal;
    private readonly MiddlewareRegistration[] _registrations;
    private readonly AnnotationScope? _annotations;
    private readonly CoordinatedPolicy[] _policies;
    private readonly bool _hasCompletionMiddleware;
    internal SchedulingCoordinator? Coordinator { get; }
    internal TaskFlow? TerminalFlow => _terminal as TaskFlow;

    public PipelineTaskScheduler(ITaskScheduler terminal)
        : this(terminal, Array.Empty<MiddlewareRegistration>(), null, (terminal as TaskFlowBase)?.MiddlewareLifetime)
    {
    }

    private PipelineTaskScheduler(ITaskScheduler terminal, MiddlewareRegistration[] registrations, AnnotationScope? annotations, TaskSchedulerMiddlewareLifetime? lifetime, SchedulingCoordinator? coordinator = null)
    {
        Lifetime = lifetime;
        Coordinator = coordinator;
        _terminal = terminal;
        _registrations = registrations;
        _annotations = annotations;
        foreach (var registration in registrations)
            if (registration.Middleware is ITaskSchedulerCompletionMiddleware)
                _hasCompletionMiddleware = true;
        _policies = coordinator == null ? Array.Empty<CoordinatedPolicy>() : System.Linq.Enumerable.ToArray(System.Linq.Enumerable.OfType<CoordinatedPolicy>(
            System.Linq.Enumerable.Select(registrations, registration => registration.Middleware)));
    }

    internal TaskSchedulerMiddlewareLifetime? Lifetime { get; }

    internal int ScheduledDrainCount
    {
        get
        {
            var count = Coordinator?.ScheduledDrainCount ?? 0;
            foreach (var registration in _registrations)
                if (registration.Middleware is BoundedAdmissionMiddleware bounded)
                    count += bounded.ScheduledDrainCount;
            return count;
        }
    }

    // Tests pause a retiring worker after idle has been published, outside the queue lock.
    internal Action? DrainIdle
    {
        set
        {
            if (Coordinator != null)
                Coordinator.DrainIdle = value;
            foreach (var registration in _registrations)
                if (registration.Middleware is BoundedAdmissionMiddleware bounded)
                    bounded.DrainIdle = value;
        }
    }

    internal PipelineTaskScheduler WithLifetime(TaskSchedulerMiddlewareLifetime lifetime)
    {
        if (Lifetime != null && Lifetime != lifetime)
            throw new InvalidOperationException("A pipeline cannot change its owning middleware lifetime.");
        return new PipelineTaskScheduler(_terminal, _registrations, _annotations, lifetime, Coordinator);
    }

    internal PipelineTaskScheduler WithKeyedScheduling(SchedulingCoordinator coordinator, ITaskScheduler terminal)
    {
        if (Coordinator != null)
            throw new InvalidOperationException("Keyed scheduling is already configured.");
        foreach (var registration in _registrations)
            if (registration.Middleware is BoundedAdmissionMiddleware)
                throw new InvalidOperationException("Configure keyed scheduling before bounded admission.");
        return new PipelineTaskScheduler(terminal, _registrations, _annotations, Lifetime, coordinator).Append(new CoordinatedPolicy());
    }

    internal void ValidateKey<TKey>(TKey key) where TKey : notnull
    {
        if (Coordinator is not IKeyedCoordinator<TKey> typed || !Coordinator.RequiresKey)
            throw new InvalidOperationException("A matching keyed scheduling registration is required.");
        typed.ValidateKey(key);
    }

    internal CoordinatedPolicy[] GetPolicies() => _policies;

    internal int CountController(BoundedAdmissionController controller)
    {
        var count = 0;
        foreach (var registration in _registrations)
            if ((registration.Middleware is BoundedAdmissionMiddleware bounded && bounded.Controller == controller) ||
                (registration.Middleware is AdmissionPolicy policy && policy.Controller == controller))
                count++;
        return count;
    }

    internal void ValidateTrySubmit()
    {
        if (_terminal is not (TaskFlow or DedicatedThreadTaskFlow or CurrentThreadTaskFlow or TaskFlowSchedulerAdapter))
            throw new NotSupportedException("TrySubmit requires a built-in terminal scheduler.");
        var budgets = 0;
        foreach (var registration in _registrations)
        {
            if (registration.Middleware is BoundedAdmissionMiddleware)
                budgets++;
            else if (registration.Middleware is ITaskSchedulerEnqueueMiddleware && registration.Middleware is not CoordinatedPolicy)
                throw new NotSupportedException("TrySubmit does not support enqueue middleware other than built-in admission and coordinated scheduling policies. Use Submit with Reject instead.");
        }
        if (Coordinator == null && budgets != 1)
            throw new NotSupportedException("TrySubmit with multiple budgets requires keyed scheduling or a concurrent branch. Configure scheduling before bounded admission.");
    }

    internal static TaskSchedulerTrySubmission<TResult> FinishTrySubmit<TResult>(AdmissionRequest request, Task<TResult> completion)
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

    public PipelineTaskScheduler Append(object middleware)
    {
        Lifetime?.CheckRegistrationOpen();
        if (Coordinator != null && middleware is BoundedAdmissionMiddleware bounded)
            middleware = new AdmissionPolicy(bounded.Controller, perKey: false);
        var registrations = new MiddlewareRegistration[_registrations.Length + 1];
        Array.Copy(_registrations, registrations, _registrations.Length);
        var registration = new MiddlewareRegistration(middleware, _annotations);
        registrations[registrations.Length - 1] = registration;
        var snapshot = new PipelineTaskScheduler(_terminal, registrations, _annotations, Lifetime, Coordinator);
        registration.Scheduler = snapshot;
        return snapshot;
    }

    public PipelineTaskScheduler WithAnnotation(Type type, IOperationAnnotation annotation)
        => new PipelineTaskScheduler(_terminal, _registrations, new AnnotationScope(_annotations, type, annotation), Lifetime, Coordinator);

    public Task<TResult> Enqueue<TResult>(Func<object?, CancellationToken, ValueTask<TResult>> taskFunc, object? state, CancellationToken cancellationToken)
    {
        return Submit(taskFunc, state, null, cancellationToken);
    }

    internal Task<TResult> Submit<TResult>(Func<object?, CancellationToken, ValueTask<TResult>> taskFunc, object? state,
        AdmissionRequest? request, CancellationToken cancellationToken)
    {
        Annotations.Argument.NotNull(taskFunc);
        if (Coordinator?.RequiresKey == true)
            throw new InvalidOperationException("A keyed pipeline requires a keyed Enqueue or Submit overload.");
        return SubmitOperation(new PipelineOperation<TResult>(taskFunc, state, _registrations.Length, _annotations, cancellationToken), request);
    }

    internal Task<TResult> SubmitKeyed<TKey, TResult>(TKey key, Func<object?, CancellationToken, ValueTask<TResult>> taskFunc,
        object? state, AdmissionRequest? request, CancellationToken cancellationToken) where TKey : notnull
    {
        Annotations.Argument.NotNull(taskFunc);
        ValidateKey(key);
        return SubmitOperation(new KeyedPipelineOperation<TKey, TResult>(key, taskFunc, state, _registrations.Length, _annotations, cancellationToken), request);
    }

    private Task<TResult> SubmitOperation<TResult>(PipelineOperation<TResult> operation, AdmissionRequest? request)
    {
        operation.AdmissionRequest = request;
        if (request != null)
            operation.OnQuiesced(request.OnQuiesced);
        return EnqueueCore(operation);
    }

    private async Task<TResult> EnqueueCore<TResult>(PipelineOperation<TResult> operation)
    {
        using var context = Lifetime?.EnterContext();
        try
        {
            var result = await EnqueueWithCompletion(operation).ConfigureAwait(false);
            operation.AdmissionRequest?.RecordCallerOutcome(null);
            return result;
        }
        catch (Exception exception)
        {
            operation.AdmissionRequest?.RecordCallerOutcome(exception);
            throw;
        }
        finally { operation.ReleaseActivity(); }
    }

    private async ValueTask<TResult> EnqueueWithCompletion<TResult>(PipelineOperation<TResult> operation)
    {
        try
        {
            if (Lifetime != null)
            {
                Lifetime.Enter();
                operation.LifetimeExit = Lifetime.Exit;
            }
            return await InvokeEnqueue(operation, _registrations.Length - 1, operation.CallerCancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Entry rejection must not invoke middleware whose resources may already be cleaned up.
            if (Lifetime != null && operation.LifetimeExit == null)
            {
                if (operation.AdmissionRequest?.Synchronous == true && exception is ObjectDisposedException)
                    operation.AdmissionRequest.Decide(TaskSchedulerAdmissionResult.Closed);
                throw;
            }
            if (!operation.TryClaimCompletion())
            {
                throw;
            }

            // Rejection/cancellation may settle inline. User completion middleware must not delay TrySubmit.
            var outcome = operation.AdmissionRequest?.Synchronous == true && _hasCompletionMiddleware
                ? await Task.Run(async () =>
                {
                    using var completionContext = Lifetime?.EnterContextIfNeeded();
                    return await InvokeCompletion(operation, 0, TaskSchedulerOperationOutcome<TResult>.FromException(exception), operation.CallerCancellationToken).ConfigureAwait(false);
                }, CancellationToken.None).ConfigureAwait(false)
                : await InvokeCompletion(operation, 0, TaskSchedulerOperationOutcome<TResult>.FromException(exception), operation.CallerCancellationToken).ConfigureAwait(false);
            return outcome.GetResultOrThrow();
        }
    }

    private async Task<TResult> InvokeEnqueue<TResult>(PipelineOperation<TResult> operation, int index, CancellationToken cancellationToken)
    {
        operation.AddActivity();
        try
        { return await InvokeEnqueueCore(operation, index, cancellationToken).ConfigureAwait(false); }
        finally { operation.ReleaseActivity(); }
    }

    private Task<TResult> InvokeEnqueueCore<TResult>(PipelineOperation<TResult> operation, int index, CancellationToken cancellationToken)
    {
        while (index >= 0 && !(_registrations[index].Middleware is ITaskSchedulerEnqueueMiddleware))
        {
            index--;
        }

        if (index < 0)
        {
            return ScheduleTerminal(operation, cancellationToken);
        }

        var registrationIndex = index;
        var registration = _registrations[index];
        if (registration.Middleware is CoordinatedPolicy)
        {
            if (operation.Coordinated)
                return InvokeEnqueue(operation, index - 1, cancellationToken);
            operation.Coordinated = true;
            return Coordinator!.Schedule(operation, GetPolicies(),
                token => InvokeEnqueue(operation, registrationIndex - 1, token), cancellationToken);
        }
        var middleware = (ITaskSchedulerEnqueueMiddleware)registration.Middleware;
        var context = new TaskSchedulerEnqueueContext<TResult>(operation, registration.Annotations, registrationIndex, cancellationToken);
        var nextCalled = 0;
        return middleware.InvokeAsync(context, nextContext =>
        {
            if (Interlocked.Exchange(ref nextCalled, 1) != 0)
                throw new InvalidOperationException("Enqueue middleware must invoke its continuation at most once.");
            return InvokeEnqueue(operation, registrationIndex - 1, nextContext.CancellationToken);
        });
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The linked source is disposed by OnQuiesced after underlying work ends, including caller-facing timeouts.")]
    private Task<TResult> ScheduleTerminal<TResult>(PipelineOperation<TResult> operation, CancellationToken cancellationToken)
    {
        if (Coordinator != null)
        {
            var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Lifetime!.StoppingToken);
            operation.OnQuiesced(linked.Dispose);
            cancellationToken = linked.Token;
        }
        operation.ProducerCancellationToken = cancellationToken;
        return _terminal.Enqueue((_, token) => ExecuteEnvelope(operation, token), operation.State, cancellationToken);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The pipeline converts every operation failure into a completion outcome.")]
    private async ValueTask<TResult> ExecuteEnvelope<TResult>(PipelineOperation<TResult> operation, CancellationToken cancellationToken)
    {
        // Reestablish ownership even when the submitting caller suppressed ExecutionContext flow.
        using var executionContext = Coordinator != null || _terminal is TaskFlowSchedulerAdapter ? Lifetime?.EnterContextIfNeeded() : null;
        TaskSchedulerOperationOutcome<TResult> outcome;
        try
        {
            var result = await InvokeExecution(operation, 0, cancellationToken).ConfigureAwait(true);
            outcome = TaskSchedulerOperationOutcome<TResult>.FromResult(result);
        }
        catch (Exception exception)
        {
            outcome = TaskSchedulerOperationOutcome<TResult>.FromException(exception);
        }

        if (operation.TryClaimCompletion())
        {
            outcome = await InvokeCompletion(operation, 0, outcome, cancellationToken).ConfigureAwait(true);
        }

        return outcome.GetResultOrThrow();
    }

    private async ValueTask<TResult> InvokeExecution<TResult>(PipelineOperation<TResult> operation, int index, CancellationToken cancellationToken)
    {
        operation.AddActivity();
        try
        { return await InvokeExecutionCore(operation, index, cancellationToken).ConfigureAwait(true); }
        finally { operation.ReleaseActivity(); }
    }

    private ValueTask<TResult> InvokeExecutionCore<TResult>(PipelineOperation<TResult> operation, int index, CancellationToken cancellationToken)
    {
        while (index < _registrations.Length && !(_registrations[index].Middleware is ITaskSchedulerExecutionMiddleware))
        {
            index++;
        }

        if (index >= _registrations.Length)
        {
            var finalContext = new TaskSchedulerOperationContext(operation, operation.FinalAnnotations, -1, cancellationToken);
            operation.MarkStarted();
            return operation.TaskFunc(operation.State, finalContext.CancellationToken);
        }

        var registrationIndex = index;
        var registration = _registrations[index];
        var middleware = (ITaskSchedulerExecutionMiddleware)registration.Middleware;
        var context = new TaskSchedulerOperationContext(operation, registration.Annotations, registrationIndex, operation.ProducerCancellationToken, registration.Scheduler);
        return middleware.InvokeAsync(context, nextContext => InvokeExecution(operation, registrationIndex + 1, nextContext.CancellationToken));
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Completion middleware failures replace the current outcome by contract.")]
    private async ValueTask<TaskSchedulerOperationOutcome<TResult>> InvokeCompletion<TResult>(
        PipelineOperation<TResult> operation,
        int index,
        TaskSchedulerOperationOutcome<TResult> outcome,
        CancellationToken cancellationToken)
    {
        while (index < _registrations.Length && !(_registrations[index].Middleware is ITaskSchedulerCompletionMiddleware))
        {
            index++;
        }

        if (index >= _registrations.Length)
        {
            return outcome;
        }

        var registrationIndex = index;
        var registration = _registrations[index];
        var middleware = (ITaskSchedulerCompletionMiddleware)registration.Middleware;
        var context = new TaskSchedulerOperationContext(operation, registration.Annotations, registrationIndex, operation.ProducerCancellationToken, registration.Scheduler);
        var nextCalled = false;
        try
        {
            return await middleware.InvokeAsync(context, outcome, async (nextContext, nextOutcome) =>
            {
                nextCalled = true;
                return await InvokeCompletion(operation, registrationIndex + 1, nextOutcome, nextContext.CancellationToken).ConfigureAwait(true);
            }).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            var replacement = TaskSchedulerOperationOutcome<TResult>.FromException(exception);
            return nextCalled
                ? replacement
                : await InvokeCompletion(operation, registrationIndex + 1, replacement, cancellationToken).ConfigureAwait(true);
        }
    }
}