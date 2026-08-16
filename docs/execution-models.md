---
layout: page
title: Execution models
permalink: /execution-models/
---

# Execution models

TaskFlow separates the scheduling contract from the lifetime-owning execution lane. Choose the smallest model that supplies the ordering and thread behavior your component actually needs.

## Standard TaskFlow

`TaskFlow` is the default choice. It serializes operations in FIFO order and schedules them through `TaskFlowOptions.TaskScheduler`, which defaults to `TaskScheduler.Default`.

```csharp
await using var flow = new TaskFlow();

Task first = flow.Enqueue(token => ProcessAsync("first", token));
Task second = flow.Enqueue(token => ProcessAsync("second", token));

await Task.WhenAll(first, second);

static Task ProcessAsync(string value, CancellationToken token) =>
    Task.CompletedTask;
```

A custom `TaskScheduler` changes where TaskFlow schedules its chain; it does not remove the flow's one-operation-at-a-time guarantee.

## DedicatedThreadTaskFlow

Use `DedicatedThreadTaskFlow` when every operation and captured asynchronous continuation must run on one library-owned background thread.

```csharp
await using var flow = new DedicatedThreadTaskFlow("device-io");

await flow.Enqueue(async token =>
{
    int beforeAwait = Environment.CurrentManagedThreadId;
    await Task.Delay(25, token);
    int afterAwait = Environment.CurrentManagedThreadId;

    Console.WriteLine($"{beforeAwait} -> {afterAwait}");
});
```

The flow creates and owns the background thread. Its synchronization context returns captured continuations to that thread. Avoid blocking the thread on asynchronous operations that need the same context.

## CurrentThreadTaskFlow

`CurrentThreadTaskFlow` uses a thread supplied by the application. Calling `Run()` starts the processing loop and blocks that thread until disposal completes.

```csharp
await using var flow = new CurrentThreadTaskFlow();
var thread = new Thread(flow.Run)
{
    IsBackground = true,
    Name = "externally-owned-lane",
};

thread.Start();
await flow.Enqueue(token => Task.Delay(25, token));
```

Use this model only when the application owns the thread and can dedicate it to the run loop. For a UI framework, its native dispatcher or synchronization-context scheduler is often the better integration point.

## TaskFlowSchedulerAdapter

`TaskFlowSchedulerAdapter` exposes an existing .NET `TaskScheduler` as an `ITaskScheduler`:

```csharp
TaskScheduler dotnetScheduler = TaskScheduler.Default;
ITaskScheduler scheduler = new TaskFlowSchedulerAdapter(dotnetScheduler);

await scheduler.Enqueue(token => Task.Delay(25, token));
```

The adapter preserves the supplied scheduler's execution characteristics. It does not create an owned FIFO flow and is not disposable. Do not assume built-in-flow cancellation or ordering semantics when using it.

## Multiple lanes and named flows

Every built-in flow is independently sequential. Two flows can execute concurrently because neither waits for the other:

```csharp
await using var imports = new TaskFlow();
await using var exports = new TaskFlow();

await Task.WhenAll(
    imports.Enqueue(token => ImportAsync(token)),
    exports.Enqueue(token => ExportAsync(token)));

static Task ImportAsync(CancellationToken token) => Task.CompletedTask;
static Task ExportAsync(CancellationToken token) => Task.CompletedTask;
```

Named dependency-injection registration selects independently configured flows; a name does not increase concurrency inside a flow. Use [Dependency injection](dependency-injection.md) for ownership and registration details.

## Selection guide

| Requirement | Choose |
|---|---|
| FIFO asynchronous work on the thread pool | `TaskFlow` |
| FIFO work and captured continuations on one owned thread | `DedicatedThreadTaskFlow` |
| FIFO work on an externally supplied dedicated thread | `CurrentThreadTaskFlow` |
| Adapt an existing .NET scheduling policy without ownership | `TaskFlowSchedulerAdapter` |
| Independent concurrent lanes | Multiple flow instances |
| A specialized queueing or concurrency policy | A custom `ITaskScheduler` or `TaskFlowBase` implementation |
