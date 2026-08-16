---
layout: page
title: Cancellation extensions
permalink: /extensions/cancellation/
---

# Cancellation extensions

Cancellation policies compose additional cancellation sources with the caller token and the underlying flow's disposal token. They request cancellation; they cannot force a delegate to stop.

## Component cancellation scopes

`CreateCancellationScope` links one shared token into every submission through the returned scheduler.

```csharp
using var lifetimeSource = new CancellationTokenSource();
await using var flow = new TaskFlow();
ITaskScheduler componentOperations =
    flow.CreateCancellationScope(lifetimeSource.Token);

using var callerSource = new CancellationTokenSource();
Task operation = componentOperations.Enqueue(
    token => Task.Delay(TimeSpan.FromSeconds(30), token),
    callerSource.Token);

lifetimeSource.Cancel();

try
{
    await operation;
}
catch (OperationCanceledException)
{
    // The component lifetime requested cancellation.
}
```

Either the caller token or scope token can cancel the linked token. Disposing the underlying built-in flow adds its own cancellation request.

## Cancel previous

`CreateCancelPrevious` requests cancellation of every older unfinished submission when a new operation is enqueued. This includes queued operations and a currently executing operation.

```csharp
await using var flow = new TaskFlow();
ITaskScheduler latest = flow.CreateCancelPrevious();

Task first = latest.Enqueue(token =>
    Task.Delay(TimeSpan.FromSeconds(30), token));

Task second = latest.Enqueue(token =>
    Task.Delay(TimeSpan.FromMilliseconds(25), token));

try
{
    await first;
}
catch (OperationCanceledException)
{
    // The second submission canceled the first.
}

await second;
```

On a built-in FIFO flow, an older queued delegate still runs when it reaches the front and receives the canceled token. A delegate that ignores cancellation can delay newer work.

## Latest-request-wins pattern

Place a cooperative delay at the start of each cancel-previous delegate. Rapid submissions cancel older delays; after a quiet period, the newest delegate proceeds.

This is a useful latest-request-wins recipe, but it is not a dedicated trailing-edge debounce implementation. In particular, a newer submission also requests cancellation of already-started work. See [Recipes](../recipes.md#latest-request-wins) and [issue #21](https://github.com/dombrovsky/TaskFlow/issues/21).

## Disposal and ownership

Neither cancellation decorator is disposable. Dispose the flow that owns the execution lane. During shutdown, continue observing returned operation tasks so expected cancellation and unexpected failures are distinguished deliberately.
