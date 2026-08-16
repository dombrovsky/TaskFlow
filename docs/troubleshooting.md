---
layout: page
title: Troubleshooting
permalink: /troubleshooting/
---

# Troubleshooting

## `CS0121` for a value-returning async lambda

TaskFlow has convenience overloads for both `Func<CancellationToken, Task<T>>` and `Func<CancellationToken, ValueTask<T>>`. Make the intended return type explicit with a named local function:

```csharp
Task<int> result = flow.Enqueue(LoadAsync);

async Task<int> LoadAsync(CancellationToken token)
{
    await Task.Delay(25, token);
    return 42;
}
```

## An operation timed out before its delegate started

`WithTimeout` measures from the wrapper's `Enqueue` call, including time spent waiting behind earlier work. Increase the budget, shorten preceding work, use separate lanes for independent resources, or put an execution-only timeout inside the delegate if that is the intended policy.

On built-in flows, the timed-out queued delegate is later invoked with an already-canceled token.

## Cancellation was requested but work continued

Cancellation is cooperative. Ensure every long-running operation accepts and observes the supplied token, including delays and downstream I/O. A delegate that ignores cancellation can hold a sequential lane after caller cancellation, timeout, or disposal.

## Disposal returned while work was still running

Synchronous disposal uses `TaskFlowOptions.SynchronousDisposeTimeout`. A finite timeout allows it to return before noncooperative work finishes. Prefer `await flow.DisposeAsync()` when shutdown must wait for completion, and use `Dispose(TimeSpan)` when the caller needs to inspect the Boolean result.

## A queued delegate ran after its caller canceled

That is expected for `TaskFlow`, `DedicatedThreadTaskFlow`, and `CurrentThreadTaskFlow`. Accepted delegates remain in the lane and receive a linked token that may already be canceled. Check the token before side effects when canceled work should do nothing.

## A background loop stopped after one failure

`OnError` observes and rethrows; it does not retry or suppress. Catch recoverable exceptions inside each loop iteration. Retain the loop's returned task so unexpected terminal failures remain observable. See the [background-loop recipe](recipes.md#own-a-recoverable-background-loop).

## Operation names are missing from logs

Place `WithOperationName` outside `WithLogging`:

```csharp
ITaskScheduler operations = flow
    .WithLogging(logger)
    .WithOperationName("orders.persist");
```

The last decorator is outermost and passes the annotation inward to logging. Reversing these calls makes logging see the operation before the name is attached.

## `WithThrottle` is unavailable on an older target

Install `TaskFlow.Extensions.Time`. The core `netstandard2.0` asset omits `WithThrottle`; the time extension package supplies it using `Microsoft.Bcl.TimeProvider` to consumers that resolve that asset.

## A named flow is still sequential

Names select independent dependency-injection configurations. They do not alter the one-operation-at-a-time behavior of a built-in flow. Use multiple named flows for independent lanes or implement a scheduler whose contract explicitly supports concurrency.

## A decorator cannot be disposed

Decorators return `ITaskScheduler` because they do not own the lane. Keep and dispose the original `ITaskFlow`, or allow the dependency-injection scope to dispose its internally owned flow.

## Submissions throw `ObjectDisposedException`

The owner has started shutting down the flow. Stop event sources and reject new component work before disposing the lane. In event-driven components, unsubscribe handlers before `DisposeAsync` so callbacks cannot race shutdown.
