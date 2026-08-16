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

## Interceptors and decorators

Prefer an `ITaskScheduler` decorator when adding a cross-cutting policy without owning the lane. Forward the original state and cancellation token unless the policy intentionally transforms them. Preserve the returned task's result and failure unless replacement behavior is part of the documented contract.

Use `ITaskSchedulerInterceptor` or `IAsyncTaskSchedulerInterceptor` for operation lifecycle callbacks. See [Observability extensions](extensions/observability.md) for callback ordering and exception replacement rules.

## Testing custom implementations

Exercise concurrent submissions, ordering, canceled-before-start work, disposal during startup and execution, delegate failures, callback failures, and operations that ignore cancellation. Run contract tests against every execution model whose guarantees the custom implementation claims.
