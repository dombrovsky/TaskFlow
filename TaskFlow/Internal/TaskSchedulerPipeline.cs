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

    public PipelineTaskScheduler(ITaskScheduler terminal)
        : this(terminal, Array.Empty<MiddlewareRegistration>(), null, (terminal as TaskFlowBase)?.MiddlewareLifetime)
    {
    }

    private PipelineTaskScheduler(ITaskScheduler terminal, MiddlewareRegistration[] registrations, AnnotationScope? annotations, TaskSchedulerMiddlewareLifetime? lifetime)
    {
        Lifetime = lifetime;
        _terminal = terminal;
        _registrations = registrations;
        _annotations = annotations;
    }

    internal TaskSchedulerMiddlewareLifetime? Lifetime { get; }

    internal PipelineTaskScheduler WithLifetime(TaskSchedulerMiddlewareLifetime lifetime)
    {
        if (Lifetime != null && Lifetime != lifetime)
            throw new InvalidOperationException("A pipeline cannot change its owning middleware lifetime.");
        return new PipelineTaskScheduler(_terminal, _registrations, _annotations, lifetime);
    }

    internal int CountController(BoundedAdmissionController controller)
    {
        var count = 0;
        foreach (var registration in _registrations)
            if (registration.Middleware is BoundedAdmissionMiddleware bounded && bounded.Controller == controller)
                count++;
        return count;
    }

    public PipelineTaskScheduler Append(object middleware)
    {
        Lifetime?.CheckRegistrationOpen();
        var registrations = new MiddlewareRegistration[_registrations.Length + 1];
        Array.Copy(_registrations, registrations, _registrations.Length);
        var registration = new MiddlewareRegistration(middleware, _annotations);
        registrations[registrations.Length - 1] = registration;
        var snapshot = new PipelineTaskScheduler(_terminal, registrations, _annotations, Lifetime);
        registration.Scheduler = snapshot;
        return snapshot;
    }

    public PipelineTaskScheduler WithAnnotation(Type type, IOperationAnnotation annotation)
        => new PipelineTaskScheduler(_terminal, _registrations, new AnnotationScope(_annotations, type, annotation), Lifetime);

    public Task<TResult> Enqueue<TResult>(Func<object?, CancellationToken, ValueTask<TResult>> taskFunc, object? state, CancellationToken cancellationToken)
    {
        return Submit(taskFunc, state, null, cancellationToken);
    }

    internal Task<TResult> Submit<TResult>(Func<object?, CancellationToken, ValueTask<TResult>> taskFunc, object? state, AdmissionRequest? request, CancellationToken cancellationToken)
    {
        Annotations.Argument.NotNull(taskFunc);
        var operation = new PipelineOperation<TResult>(taskFunc, state, _registrations.Length, _annotations, cancellationToken)
        {
            AdmissionRequest = request
        };
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
                throw;
            if (!operation.TryClaimCompletion())
            {
                throw;
            }

            var outcome = await InvokeCompletion(operation, 0, TaskSchedulerOperationOutcome<TResult>.FromException(exception), operation.CallerCancellationToken).ConfigureAwait(false);
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

    private Task<TResult> ScheduleTerminal<TResult>(PipelineOperation<TResult> operation, CancellationToken cancellationToken)
    {
        operation.ProducerCancellationToken = cancellationToken;
        return _terminal.Enqueue((_, token) => ExecuteEnvelope(operation, token), operation.State, cancellationToken);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The pipeline converts every operation failure into a completion outcome.")]
    private async ValueTask<TResult> ExecuteEnvelope<TResult>(PipelineOperation<TResult> operation, CancellationToken cancellationToken)
    {
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