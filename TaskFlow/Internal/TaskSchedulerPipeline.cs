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
        _policies = coordinator == null ? Array.Empty<CoordinatedPolicy>() : System.Linq.Enumerable.ToArray(System.Linq.Enumerable.OfType<CoordinatedPolicy>(
            System.Linq.Enumerable.Select(registrations, registration => registration.Middleware)));
    }

    internal TaskSchedulerMiddlewareLifetime? Lifetime { get; }

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
        // A built-in terminal already rejects closed entry and drains every accepted delegate.
        // An annotation-only snapshot has no middleware or admission outcome to track.
        if (_registrations.Length == 0 && request == null && Coordinator == null && _terminal is TaskFlow or DedicatedThreadTaskFlow or CurrentThreadTaskFlow)
            return _terminal.Enqueue(taskFunc, state, cancellationToken);
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
        {
            operation.OnQuiesced(request.OnQuiesced);
            return EnqueueCore(operation);
        }
        // Plain enqueue invokes forwarding before handing ownership to the task observer.
        Task<TResult> pending;
        try
        {
            if (Lifetime != null)
            {
                Lifetime.Enter();
                operation.Lifetime = Lifetime;
            }
            pending = InvokeEnqueueCore(operation, _registrations.Length - 1, operation.CallerCancellationToken);
            Annotations.Argument.NotNull(pending);
        }
        catch
        {
            operation.ReleaseActivity();
            throw;
        }
        return EnqueueCore(operation, pending);
    }

    private async Task<TResult> EnqueueCore<TResult>(PipelineOperation<TResult> operation, Task<TResult>? pending = null)
    {
        try
        {
            TResult result;
            try
            {
                if (pending == null)
                {
                    if (Lifetime != null)
                    {
                        Lifetime.Enter();
                        operation.Lifetime = Lifetime;
                    }
                    // Explicit submissions report admission and scheduling failures through their tasks.
                    pending = InvokeEnqueueCore(operation, _registrations.Length - 1, operation.CallerCancellationToken);
                }
                result = await pending.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                if (Lifetime != null && operation.Lifetime == null)
                {
                    if (operation.AdmissionRequest?.Synchronous == true && exception is ObjectDisposedException)
                        operation.AdmissionRequest.Decide(TaskSchedulerAdmissionResult.Closed);
                    throw;
                }
                if (!operation.TryClaimCompletion())
                    throw;
                // Rejection/cancellation may settle inline. Keep user completion middleware off TrySubmit's stack.
                var deferCompletion = operation.AdmissionRequest?.Synchronous == true
                    && Array.Exists(_registrations, static registration => registration.Middleware is ITaskSchedulerCompletionMiddleware);
                var outcome = deferCompletion
                    ? await Task.Run(async () =>
                        await InvokeCompletion(operation, 0, TaskSchedulerOperationOutcome<TResult>.FromException(exception), operation.ProducerCancellationToken).ConfigureAwait(false), CancellationToken.None).ConfigureAwait(false)
                    : await InvokeCompletion(operation, 0, TaskSchedulerOperationOutcome<TResult>.FromException(exception), operation.ProducerCancellationToken).ConfigureAwait(false);
                result = outcome.GetResultOrThrow();
            }
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

        return InvokeEnqueueMiddleware(operation, index, cancellationToken);
    }

    private Task<TResult> InvokeEnqueueMiddleware<TResult>(PipelineOperation<TResult> operation, int index, CancellationToken cancellationToken)
    {
        var registrationIndex = index;
        var registration = _registrations[index];
        if (registration.Middleware is CoordinatedPolicy)
        {
            if (operation.Coordinated)
                return InvokeEnqueueCore(operation, index - 1, cancellationToken);
            operation.Coordinated = true;
            return Coordinator!.Schedule(operation, GetPolicies(),
                token => InvokeEnqueueCore(operation, registrationIndex - 1, token), cancellationToken);
        }
        var middleware = (ITaskSchedulerEnqueueMiddleware)registration.Middleware;
        var context = new TaskSchedulerEnqueueContext<TResult>(operation, registration.Annotations, registrationIndex, cancellationToken);
        var nextCalled = 0;
        return middleware.InvokeAsync(context, nextContext =>
        {
            if (Interlocked.Exchange(ref nextCalled, 1) != 0)
                throw new InvalidOperationException("Enqueue middleware must invoke its continuation at most once.");
            return InvokeEnqueueCore(operation, registrationIndex - 1, nextContext.CancellationToken);
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
        // One execution envelope owns the entire structured execution/completion tree.
        operation.AddActivity();
        try
        {
            TaskSchedulerOperationOutcome<TResult> outcome;
            try
            {
                var result = await InvokeExecutionCore(operation, 0, cancellationToken).ConfigureAwait(true);
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
            operation.MarkStarted();
            return operation.TaskFunc(operation.State, cancellationToken);
        }

        return InvokeExecutionMiddleware(operation, index, cancellationToken);
    }

    private ValueTask<TResult> InvokeExecutionMiddleware<TResult>(PipelineOperation<TResult> operation, int index, CancellationToken cancellationToken)
    {
        var registrationIndex = index;
        var registration = _registrations[index];
        var middleware = (ITaskSchedulerExecutionMiddleware)registration.Middleware;
        var context = new TaskSchedulerOperationContext(operation, registration.Annotations, registrationIndex, cancellationToken, registration.Scheduler);
        return middleware.InvokeAsync(context, nextContext =>
        {
            ValidateOperationContext(operation, nextContext);
            return InvokeExecutionCore(operation, registrationIndex + 1, nextContext.CancellationToken);
        });
    }

    private ValueTask<TaskSchedulerOperationOutcome<TResult>> InvokeCompletion<TResult>(
        PipelineOperation<TResult> operation,
        int index,
        TaskSchedulerOperationOutcome<TResult> outcome,
        CancellationToken cancellationToken,
        CancellationToken? producerCancellationToken = null)
    {
        var producerToken = producerCancellationToken ?? operation.ProducerCancellationToken;
        while (index < _registrations.Length && !(_registrations[index].Middleware is ITaskSchedulerCompletionMiddleware))
        {
            index++;
        }

        if (index >= _registrations.Length)
        {
            return new ValueTask<TaskSchedulerOperationOutcome<TResult>>(outcome);
        }

        return InvokeCompletionMiddleware(operation, index, outcome, cancellationToken, producerToken);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Completion middleware failures replace the current outcome by contract.")]
    private async ValueTask<TaskSchedulerOperationOutcome<TResult>> InvokeCompletionMiddleware<TResult>(
        PipelineOperation<TResult> operation,
        int index,
        TaskSchedulerOperationOutcome<TResult> outcome,
        CancellationToken cancellationToken,
        CancellationToken producerToken)
    {
        var registrationIndex = index;
        var registration = _registrations[index];
        var middleware = (ITaskSchedulerCompletionMiddleware)registration.Middleware;
        var context = new TaskSchedulerOperationContext(operation, registration.Annotations, registrationIndex, cancellationToken, registration.Scheduler, producerToken);
        var nextCalled = false;
        try
        {
            return await middleware.InvokeAsync(context, outcome, (nextContext, nextOutcome) =>
            {
                ValidateOperationContext(operation, nextContext);
                nextCalled = true;
                return InvokeCompletion(operation, registrationIndex + 1, nextOutcome, nextContext.CancellationToken, producerToken);
            }).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            var replacement = TaskSchedulerOperationOutcome<TResult>.FromException(exception);
            return nextCalled
                ? replacement
                : await InvokeCompletion(operation, registrationIndex + 1, replacement, cancellationToken, producerToken).ConfigureAwait(true);
        }
    }

    private static void ValidateOperationContext(PipelineOperation operation, TaskSchedulerOperationContext context)
    {
        if (!ReferenceEquals(operation, context.Operation))
            throw new InvalidOperationException("A middleware continuation must receive a context for the same operation.");
    }
}