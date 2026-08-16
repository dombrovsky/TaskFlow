---
layout: page
title: Extensions
permalink: /extensions/
---

# Extensions

Extensions return `ITaskScheduler` decorators. They do not own or dispose the scheduler or flow they wrap. Keep the underlying `ITaskFlow` reference and dispose it at the owner boundary.

| Extension | Package and frameworks | Effect | Details |
|---|---|---|---|
| `CreateCancelPrevious` | `TaskFlow`; all targets | Cancels older unfinished submissions | [Cancellation](cancellation.md) |
| `CreateCancellationScope` | `TaskFlow`; all targets | Links a shared lifetime token | [Cancellation](cancellation.md) |
| `WithTimeout` | `TaskFlow`; all targets | Adds a queue-and-execution timeout | [Reliability](reliability.md) |
| `WithThrottle` | `TaskFlow` on .NET 8/10; `TaskFlow.Extensions.Time` on `netstandard2.0` | Rejects submissions inside an admission interval | [Reliability](reliability.md) |
| `OnError` | `TaskFlow`; all targets | Observes matching failures and rethrows | [Reliability](reliability.md) |
| `WithOperationName` | `TaskFlow`; all targets | Adds an operation-name annotation | [Observability](observability.md) |
| `Intercept` | `TaskFlow`; all targets | Runs synchronous or asynchronous lifecycle callbacks | [Observability](observability.md) |
| `WithLogging` | `TaskFlow.Microsoft.Extensions.Logging`; all targets | Emits structured lifecycle events | [Observability](observability.md) |

## Composition

Decorator order is observable. The last extension call produces the outermost scheduler and sees a submission first.

```csharp
await using var flow = new TaskFlow();

ITaskScheduler operations = flow
    .WithLogging(logger)
    .WithTimeout(TimeSpan.FromSeconds(10))
    .CreateCancellationScope(lifetimeToken)
    .WithOperationName("orders.persist");

await operations.Enqueue(token => PersistAsync(token));
```

Here the operation-name annotation travels inward to timeout and logging. The caller token, lifetime token, timeout signal, and flow-disposal token can all request cancellation. The delegate must cooperate with the token it receives.

```csharp
static Task PersistAsync(CancellationToken token) => Task.CompletedTask;
```

## Common rules

- Await or return an operation task when its outcome matters to the caller. For intentional fire-and-forget work, discard it explicitly and add failure reporting inside the operation or through a decorator when needed.
- Dispose the underlying flow, not its decorators.
- Treat cancellation as a request rather than proof that work stopped.
- Use named local functions for value-returning asynchronous delegates if overload resolution is ambiguous.
- Test policy ordering because moving one decorator can change which annotations or failures another decorator observes.
