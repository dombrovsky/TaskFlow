---
layout: page
title: Getting started
permalink: /getting-started/
---

# Getting started

Install the core package:

```shell
dotnet add package TaskFlow
```

All public types use the `System.Threading.Tasks.Flow` namespace.

## Create a FIFO execution lane

```csharp
using System.Threading.Tasks.Flow;

await using var flow = new TaskFlow();

Task first = flow.Enqueue(async token =>
{
    await Task.Delay(25, token);
    Console.WriteLine("first");
});

Task second = flow.Enqueue(token =>
{
    Console.WriteLine("second");
    return Task.CompletedTask;
});

await Task.WhenAll(first, second);
```

Calls to `Enqueue` are thread-safe. The standard `TaskFlow` invokes one operation at a time in submission order. Each call returns a task representing that operation's result, exception, or cancellation.

## Choose whether to await an operation

TaskFlow keeps the lane moving after an operation fails. Await or return the task when the operation's result, cancellation, or exception belongs to a caller:

```csharp
Task write = flow.Enqueue(token => WriteAsync(token));

try
{
    await write;
}
catch (IOException exception)
{
    Console.Error.WriteLine(exception.Message);
}

static Task WriteAsync(CancellationToken token) => Task.CompletedTask;
```

For component-owned fire-and-forget work, discard the task explicitly with `_ = flow.Enqueue(...)`. Disposing the flow still requests cancellation and waits for accepted work, but it does not surface an ignored operation's exception. Handle failures inside the operation or add an error-observation decorator when diagnostics are required.

## Prefer asynchronous disposal

Use `await using` when the owner has an asynchronous lifetime. `DisposeAsync` stops accepting work, requests cancellation through the operation tokens, and waits for the lane to finish. Cancellation remains cooperative: an operation that ignores its token can delay disposal indefinitely.

Synchronous `Dispose()` waits only for `TaskFlowOptions.SynchronousDisposeTimeout`. It can return while noncooperative work continues. See [Concepts and lifecycle](concepts-and-lifecycle.md) and [Semantics and pitfalls](semantics-and-pitfalls.md) before using a flow to own long-running work.

## Pass caller cancellation

```csharp
using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));

await flow.Enqueue(
    token => Task.Delay(TimeSpan.FromSeconds(1), token),
    cancellationSource.Token);
```

For built-in flows, cancellation does not remove a queued delegate. When it reaches the front of the lane, the delegate is invoked with an already-canceled token and decides cooperatively how to finish.

## Next steps

- Decide whether TaskFlow fits your case in [Choosing TaskFlow](choosing-taskflow.md).
- Start from a complete application pattern in [Recipes](recipes.md).
- Add cancellation and reliability policies through [Extensions](extensions/index.md).
- Select a different thread or scheduler in [Execution models](execution-models.md).
- Register scoped or named lanes with [Dependency injection](dependency-injection.md).
