---
layout: page
title: Concepts and lifecycle
permalink: /concepts-and-lifecycle/
---

# Concepts and lifecycle

## Scheduler and flow

`ITaskScheduler` is the minimal submission contract. It accepts a delegate, optional state, and cancellation token, then returns a task for that operation. A custom implementation may choose any scheduling policy.

`ITaskFlow` adds ownership to that contract through `IAsyncDisposable`, `IDisposable`, `ITaskFlowInfo`, and `Dispose(TimeSpan)`. Built-in flows own their execution lane; scheduler decorators such as `WithTimeout` and `OnError` do not.

Keep both references when composing policies:

```csharp
await using ITaskFlow flow = new TaskFlow();
ITaskScheduler operations = flow
    .WithTimeout(TimeSpan.FromSeconds(10))
    .OnError<Exception>(exception => Console.Error.WriteLine(exception));

await operations.Enqueue(token => Task.Delay(25, token));
```

Dispose `flow`, not `operations`.

## FIFO and operation completion

`TaskFlow`, `DedicatedThreadTaskFlow`, and `CurrentThreadTaskFlow` execute accepted operations one at a time in FIFO order. An operation begins after its predecessor finishes, including failure or cancellation. The task returned by `Enqueue` completes with that operation's own outcome.

FIFO describes invocation order within one flow. It does not impose ordering across multiple flows, and it is not a universal guarantee of every `ITaskScheduler` implementation.

## Cancellation sources

A built-in flow links the caller's token with its disposal token. Decorators can add more sources:

- `CreateCancellationScope` adds a component or request lifetime token.
- `CreateCancelPrevious` requests cancellation of older unfinished submissions.
- `WithTimeout` adds a timeout cancellation signal and reports a `TimeoutException` if the timer wins.

Cancellation is a request. Delegates must observe the supplied token. Built-in flows still invoke a queued delegate after cancellation so the lane can advance deterministically.

## Failure continuity

An operation failure is exposed through its returned task. Built-in flows wait for that task, suppress its failure only in the internal predecessor chain, and then invoke the next delegate. `OnError` can observe matching failures, but it rethrows them to the returned operation task.

## Disposal sequence

`DisposeAsync`:

1. prevents new submissions;
2. finishes pending initialization where applicable;
3. cancels the flow lifetime token;
4. waits for queued and active operations to finish; and
5. releases owned resources.

`Dispose()` performs the same shutdown attempt but waits no longer than `TaskFlowOptions.SynchronousDisposeTimeout`. `Dispose(TimeSpan)` reports whether completion occurred within its explicit timeout.

After disposal begins, new submissions throw `ObjectDisposedException`. Retain operation tasks if callers need their individual shutdown or failure outcomes; disposal intentionally does not surface every operation exception.

## Synchronization context

Thread-backed flows install a synchronization context that marshals captured continuations back to their execution thread. The standard `TaskFlow` schedules each operation through its configured `TaskScheduler`; code inside a delegate follows normal `await` and synchronization-context rules.

See [Execution models](execution-models.md) for the differences among the built-in flows and `TaskFlowSchedulerAdapter`.
