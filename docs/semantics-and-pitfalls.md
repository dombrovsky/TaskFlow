---
layout: page
title: Semantics and pitfalls
permalink: /semantics-and-pitfalls/
---

# Semantics and pitfalls

For a first-pass decision on whether these tradeoffs match your scenario, see [Choosing TaskFlow](choosing-taskflow.md).

## Canceled queued delegates are still invoked

The built-in `TaskFlow`, `DedicatedThreadTaskFlow`, and `CurrentThreadTaskFlow` implementations preserve queue progression by invoking every accepted delegate after its predecessor finishes. If cancellation happened while the operation waited, the delegate receives an already-canceled token.

```csharp
await using var flow = new TaskFlow();
using var cancellationSource = new CancellationTokenSource();

Task blocker = flow.Enqueue(async token =>
{
    await Task.Delay(50, token);
});

Task canceled = flow.Enqueue(token =>
{
    token.ThrowIfCancellationRequested();
    return Task.CompletedTask;
}, cancellationSource.Token);

cancellationSource.Cancel();
await blocker;

try
{
    await canceled;
}
catch (OperationCanceledException)
{
    // The delegate ran and observed its already-canceled token.
}
```

This is a guarantee of the built-in flows, not of arbitrary `ITaskScheduler` implementations or adapters.

## Timeout includes queue waiting

`WithTimeout` starts its timer when `Enqueue` reaches the wrapper. Time spent behind earlier operations consumes the same budget as delegate execution. The returned task can time out before the delegate reaches the front of a busy built-in flow; that delegate is later invoked with timeout cancellation already requested.

The timeout is cooperative. A delegate that ignores the token can continue running after the caller receives `TimeoutException`, and the sequential lane cannot move to later work until that delegate actually finishes.

## Synchronous disposal can return early

`DisposeAsync` waits for full completion. Synchronous disposal is bounded by `TaskFlowOptions.SynchronousDisposeTimeout`; the default is infinite, but a configured finite value can expire while work continues. Use `Dispose(TimeSpan)` when the caller needs the Boolean completion result.

Prefer asynchronous disposal for component-owned background work and make long-running delegates observe cancellation promptly.

## Disposal owns lifetime, not operation outcomes

Disposal waits for lane completion and suppresses operation failures internally. This makes deliberate fire-and-forget possible: a component can discard selected operation tasks and still use flow disposal to request cancellation and wait for accepted work.

Disposal does not propagate an ignored operation's exception. Await or return the task when its outcome belongs to a caller. For intentionally discarded work, handle failures inside the delegate or use a decorator such as `OnError` to report them. `OnError` observes and rethrows, so its diagnostic side effect still runs even when the returned task is deliberately ignored.

## Decorator order changes what a policy sees

Decorators wrap from left to right. In this chain, `WithOperationName` is outermost, so its annotation reaches the logging decorator:

```csharp
ITaskScheduler operations = flow
    .WithLogging(logger)
    .WithTimeout(TimeSpan.FromSeconds(10))
    .WithOperationName("orders.persist");
```

Moving `WithOperationName` inside `WithLogging` prevents that logging wrapper from seeing the annotation. Cancellation and error wrappers can likewise observe different failures depending on their order.

## Value-returning async lambdas can be ambiguous

TaskFlow provides both `Task<T>` and `ValueTask<T>` convenience overloads. A directly supplied value-returning `async` lambda can produce compiler error `CS0121`. Give the return type explicitly with a named local function:

```csharp
Task<int> result = flow.Enqueue(LoadAsync);

async Task<int> LoadAsync(CancellationToken token)
{
    await Task.Delay(25, token);
    return 42;
}
```

## Throttle, latest-wins, and debounce differ

- `WithThrottle` is leading-edge admission throttling. It accepts the first submission and rejects later submissions during the interval with `OperationThrottledException`.
- `CreateCancelPrevious` requests cancellation of older unfinished work whenever newer work is submitted. Adding an initial delay creates a latest-request-wins pattern when delegates cooperate.
- Trailing-edge debounce waits for a quiet interval and eventually executes the latest submission. TaskFlow does not currently expose that policy; it is tracked in [issue #21](https://github.com/dombrovsky/TaskFlow/issues/21).
