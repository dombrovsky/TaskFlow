---
layout: page
title: Customization
permalink: /customization/
---

# Customization

Use customization when a built-in FIFO flow, decorator chain, or .NET `TaskScheduler` adapter cannot express the required policy. Custom implementations are responsible for defining their own ordering, cancellation, and ownership guarantees.

## Implement ITaskScheduler

`ITaskScheduler` is the smallest extension point. This example adapts an immediate execution policy:

```csharp
public sealed class InlineScheduler : ITaskScheduler
{
    public async Task<T> Enqueue<T>(
        Func<object?, CancellationToken, ValueTask<T>> taskFunc,
        object? state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(taskFunc);
        return await taskFunc(state, cancellationToken);
    }
}
```

This scheduler is not FIFO, owned, or disposable. Document such differences because the built-in-flow guarantees do not automatically apply to `ITaskScheduler` implementations.

At minimum, a production scheduler should define:

- whether submissions are FIFO, concurrent, prioritized, rejected, or coalesced;
- when and where delegates are invoked;
- what happens when cancellation precedes invocation;
- how delegate results, failures, and cancellation complete returned tasks; and
- whether it owns resources and how shutdown works.

## Adapt a .NET TaskScheduler

When an existing `TaskScheduler` already defines the execution environment, use `TaskFlowSchedulerAdapter`:

```csharp
TaskScheduler dotnetScheduler = TaskScheduler.Default;
ITaskScheduler scheduler = new TaskFlowSchedulerAdapter(dotnetScheduler);

await scheduler.Enqueue(token => Task.Delay(25, token));
```

The adapter does not add FIFO serialization or lifetime ownership.

## Derive from TaskFlowBase

Derive from `TaskFlowBase` only when the implementation needs an owned lifecycle. A derived flow must:

- make `Enqueue` thread-safe, validate delegates, call `CheckDisposed`, and return an operation task;
- link caller cancellation with `CompletionToken` where flow disposal should request cancellation;
- use `Starting()` and `Ready()` to report initialization state when startup is asynchronous;
- implement `GetInitializationTask()` and `GetCompletionTask()` accurately;
- preserve completion progress after individual operation failures; and
- release owned resources through the disposal hooks without losing observable operation outcomes.

Use `ThisLock` for state coordinated with the base disposal state. Do not claim built-in FIFO or canceled-delegate behavior unless the custom implementation actually preserves it and tests it.

## Middleware and terminal schedulers

Implement `ITaskScheduler` when you need a new terminal scheduling strategy. Implement middleware when the terminal scheduling strategy is already correct and you only need to add admission, execution, or outcome policy. Middleware does not require changes to `ITaskScheduler.Enqueue`, so it can be placed around built-in flows and third-party schedulers.

The pipeline has three phases:

| Phase | Runs | Typical uses | Continuation behavior |
| --- | --- | --- | --- |
| Enqueue | Before the terminal accepts the operation | admission, coalescing, throttling, cancellation-token transformation | Call zero or one time |
| Execution | Inside the delegate invoked by the terminal | instrumentation, retries, operation wrapping | Call one or more times |
| Completion | After a result or exception is available | error handling, result transformation, final telemetry | Normally call exactly once |

Implement one or more of these interfaces:

- `ITaskSchedulerEnqueueMiddleware`
- `ITaskSchedulerExecutionMiddleware`
- `ITaskSchedulerCompletionMiddleware`

Every middleware also implements the marker interface `ITaskSchedulerMiddleware`. Register it with `UseMiddleware`. A single-phase object registers that phase; a compound object atomically registers every phase it implements.

### Register middleware

Registration returns a new immutable scheduler snapshot. It does not modify the source scheduler:

```csharp
ITaskScheduler terminal = new TaskFlow();

ITaskScheduler observed = terminal
    .UseMiddleware(new TraceExecutionMiddleware())
    .UseMiddleware(new TraceCompletionMiddleware());

// `terminal` has no middleware. `observed` has both registrations.
```

Snapshots may be reused concurrently and branched safely:

```csharp
ITaskScheduler common = terminal.UseMiddleware(new MetricsMiddleware());
ITaskScheduler interactive = common.UseMiddleware(new InteractiveAdmissionMiddleware());
ITaskScheduler background = common.UseMiddleware(new BackgroundAdmissionMiddleware());
```

The snapshot is non-owning. Disposing a terminal that implements `IDisposable` remains the caller's responsibility. A pipeline never disposes the terminal, middleware objects, or collaborators held by middleware.

### Phase ordering

For registrations `A`, then `B`, the phases run in this order:

```text
enqueue:    B -> A -> terminal
execution:  terminal -> A -> B -> operation
completion: terminal outcome -> A -> B -> caller
```

Enqueue uses reverse registration order because the newest admission policy is the outermost policy. Execution and completion use registration order. A compound middleware occupies one registration position in every phase it implements.

An existing scheduler wrapper that does not expose the middleware pipeline is an opaque boundary. Adding middleware outside that wrapper creates a new pipeline; TaskFlow does not inspect, flatten, or reorder the wrapper and any pipeline hidden inside it.

### Enqueue middleware

Enqueue middleware receives immutable operation facts and decides whether or how to continue toward the terminal:

```csharp
public sealed class RejectWhenStopping : ITaskSchedulerEnqueueMiddleware
{
    private readonly Func<bool> _isStopping;

    public RejectWhenStopping(Func<bool> isStopping) => _isStopping = isStopping;

    public Task<TResult> InvokeAsync<TResult>(
        TaskSchedulerEnqueueContext<TResult> context,
        TaskSchedulerEnqueueDelegate<TResult> continuation)
    {
        if (_isStopping())
            return Task.FromException<TResult>(new InvalidOperationException("The service is stopping."));

        return continuation(context);
    }
}
```

Returning without invoking `continuation` prevents terminal scheduling. This supports rejection and shared-work policies. Invoke the enqueue continuation at most once: the terminal contract represents one submitted operation.

Middleware placement also defines an observation boundary. Enqueue middleware outside a short-circuiting registration observes every submission. Execution and completion middleware inside that registration observes only work that reaches the terminal. For a future shared-work policy, followers may therefore be visible to outer admission metrics while inner execution metrics run once for the shared producer.

`context.CallerCancellationToken` is always the token supplied by the caller. `context.CancellationToken` is the effective producer token currently flowing toward the terminal. To transform only the producer token, pass a copied context onward:

```csharp
return continuation(context.WithCancellationToken(producerToken));
```

`WithCancellationToken` retains the original state, caller token, annotations, operation identity, and registration-local state. Middleware that separates a caller's wait from shared producer work should observe `CallerCancellationToken` for the wait and pass the shared producer token through `WithCancellationToken`.

### Execution middleware

Execution middleware runs on the context chosen by the terminal scheduler. It can surround the operation in the same way application middleware surrounds a request:

```csharp
public sealed class TraceExecutionMiddleware : ITaskSchedulerExecutionMiddleware
{
    public async ValueTask<TResult> InvokeAsync<TResult>(
        TaskSchedulerOperationContext context,
        TaskSchedulerExecutionDelegate<TResult> continuation)
    {
        Console.WriteLine("Starting");
        try
        {
            return await continuation(context).ConfigureAwait(true);
        }
        finally
        {
            Console.WriteLine("Finished execution");
        }
    }
}
```

Ordinary middleware should invoke `continuation` once. Orchestration middleware may invoke it repeatedly—for example, to implement retry—without enqueueing another terminal delegate. Such middleware owns the semantics of repeated operation execution, including which failures are retryable and how cancellation is handled.

Use `ConfigureAwait(true)` when asynchronous middleware must preserve the synchronization context or scheduler affinity established by the terminal.

### Completion middleware and outcomes

Completion middleware receives a `TaskSchedulerOperationOutcome<TResult>`. Inspect `IsSuccess` before reading `Result`; `Exception` is non-null for a failed outcome.

```csharp
public sealed class TraceCompletionMiddleware : ITaskSchedulerCompletionMiddleware
{
    public ValueTask<TaskSchedulerOperationOutcome<TResult>> InvokeAsync<TResult>(
        TaskSchedulerOperationContext context,
        TaskSchedulerOperationOutcome<TResult> outcome,
        TaskSchedulerCompletionDelegate<TResult> continuation)
    {
        if (!outcome.IsSuccess)
            Console.Error.WriteLine(outcome.Exception);

        return continuation(context, outcome);
    }
}
```

Pass the same outcome onward to preserve an exception's identity and captured stack. Use `TaskSchedulerOperationOutcome<TResult>.FromResult` or `FromException` only when intentionally replacing the current result or failure. If completion middleware throws, that exception replaces the current outcome and later completion middleware sees the replacement.

Completion is claimed atomically and runs at most once for an operation, including races between rejection, timeout, and the scheduled delegate. Failures produced during terminal execution are completed on the terminal context. Failures raised before the terminal accepts or invokes the operation cannot claim terminal thread or synchronization-context affinity.

### Share per-operation state between phases

One middleware object may implement multiple phase interfaces. Register it once with `UseMiddleware` to give all of its phases one operation-local state slot:

```csharp
public sealed class TimingMiddleware :
    ITaskSchedulerExecutionMiddleware,
    ITaskSchedulerCompletionMiddleware
{
    private sealed class TimingState
    {
        public Stopwatch Stopwatch { get; } = new Stopwatch();
    }

    public async ValueTask<TResult> InvokeAsync<TResult>(
        TaskSchedulerOperationContext context,
        TaskSchedulerExecutionDelegate<TResult> continuation)
    {
        context.GetOrCreateLocalState(() => new TimingState()).Stopwatch.Start();
        return await continuation(context).ConfigureAwait(true);
    }

    public ValueTask<TaskSchedulerOperationOutcome<TResult>> InvokeAsync<TResult>(
        TaskSchedulerOperationContext context,
        TaskSchedulerOperationOutcome<TResult> outcome,
        TaskSchedulerCompletionDelegate<TResult> continuation)
    {
        TimingState? state = context.GetLocalState<TimingState>();
        state?.Stopwatch.Stop();
        Console.WriteLine(state?.Stopwatch.Elapsed);
        return continuation(context, outcome);
    }
}
```

The slot is isolated per scheduled operation and per registration. `GetOrCreateLocalState<TState>` creates its value atomically. A different state type cannot later occupy the same registration slot. Registering the same middleware object twice creates two independent slots; registering its phases separately also creates separate slots.

Middleware objects themselves are shared by all operations and must therefore keep any mutable registration-wide state thread-safe. Put operation-specific mutable state in the context slot rather than in middleware instance fields.

### Middleware author invariants

Custom middleware should preserve these composition rules:

- Treat scheduler configuration and contexts as immutable; return a new scheduler snapshot or pass a copied context instead of mutating shared data.
- Invoke an enqueue continuation zero or one time. A zero-call path must return an explicit result, cancellation, rejection, or shared task.
- Document execution middleware that invokes its continuation more than once. Every invocation may run all downstream execution middleware and the user operation again within the same terminal queue turn.
- Pass one final outcome through completion middleware. Forward an existing outcome unchanged unless intentionally replacing its result or exception.
- Keep registration-wide shared state concurrency-safe and operation-specific state in the registration-local context slot.
- Do not dispose the terminal scheduler, coordinators, lanes, or other collaborators unless a separate API explicitly transfers their ownership.
- Preserve the distinction between caller-wait cancellation and producer cancellation when sharing work between callers.

### Forward-scoped annotations

Annotations are immutable metadata scopes keyed by their registered type:

```csharp
public sealed class TenantAnnotation : IOperationAnnotation
{
    public TenantAnnotation(string tenantId) => TenantId = tenantId;
    public string TenantId { get; }
}

ITaskScheduler tenantPipeline = terminal
    .WithAnnotation(new TenantAnnotation("north"))
    .UseMiddleware(new TenantTelemetryMiddleware());
```

A middleware registration captures only annotations added before that registration. Later annotations are visible only to later registrations and to the final context-aware operation. Adding another annotation with the same registered type shadows the earlier value without changing existing registrations or sibling pipeline branches.

Read captured metadata with `context.GetAnnotation<TAnnotation>()`. The lookup uses the exact type supplied to `WithAnnotation<TAnnotation>`; registering an implementation as an interface and querying by its concrete type are different keys.

Use `AnnotatedEnqueue` when the final operation needs an annotation from the final scope:

```csharp
string tenant = await tenantPipeline.AnnotatedEnqueue<string, TenantAnnotation>(
    (state, annotation, token) => new ValueTask<string>(
        annotation?.TenantId ?? "unknown"),
    state: null,
    cancellationToken: CancellationToken.None);
```

### Choosing middleware or interceptors

Use middleware for reusable policy that needs to affect admission, wrap execution, transform outcomes, carry metadata, or coordinate state across phases. Use `ITaskSchedulerInterceptor` or `IAsyncTaskSchedulerInterceptor` for the established operation lifecycle callback model. The built-in interception, error, timeout, throttling, cancellation, and logging extensions are composed through the middleware pipeline but retain their existing public APIs. See [Observability extensions](extensions/observability.md) for callback ordering and exception replacement rules.

## Testing custom implementations

Exercise concurrent submissions, ordering, canceled-before-start work, disposal during startup and execution, delegate failures, callback failures, and operations that ignore cancellation. Run contract tests against every execution model whose guarantees the custom implementation claims.
