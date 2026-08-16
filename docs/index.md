---
layout: home
title: TaskFlow for .NET
permalink: /
---

TaskFlow is an owned FIFO execution lane for asynchronous .NET work. Each submitted operation gets its own result task while the lane serializes access, preserves submission order, and provides a clear shutdown boundary.

Use TaskFlow to:

- serialize calls to mutable or non-thread-safe resources;
- turn synchronous callbacks into ordered asynchronous processing;
- bind background work to a component or dependency-injection scope;
- cancel obsolete work when a newer operation arrives;
- compose timeout, cancellation, logging, annotations, and error observation; and
- run work on the thread pool, a dedicated thread, or a caller-owned thread.

## Start by goal

| Goal | Read |
|---|---|
| Create and dispose a first FIFO lane | [Getting started](getting-started.md) |
| Understand operation completion and ownership | [Concepts and lifecycle](concepts-and-lifecycle.md) |
| Avoid cancellation, timeout, and disposal surprises | [Semantics and pitfalls](semantics-and-pitfalls.md) |
| Choose the thread pool, a dedicated thread, or another scheduler | [Execution models](execution-models.md) |
| Apply TaskFlow to common application problems | [Recipes](recipes.md) |
| Add cancellation, reliability, and diagnostics policies | [Extensions](extensions/index.md) |
| Register scoped or named flows | [Dependency injection](dependency-injection.md) |
| Implement adapters or custom flows | [Customization](customization.md) |
| Check framework and package availability | [Compatibility](compatibility.md) |
| Diagnose common integration problems | [Troubleshooting](troubleshooting.md) |

## Install

```shell
dotnet add package TaskFlow
```

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

`second` starts only after `first` completes. A failure in one returned task does not stop later queued operations.

## Source and packages

- [GitHub repository](https://github.com/dombrovsky/TaskFlow)
- [TaskFlow on NuGet](https://www.nuget.org/packages/TaskFlow/)
- [License](https://github.com/dombrovsky/TaskFlow/blob/main/LICENSE)
- [Issues and feedback](https://github.com/dombrovsky/TaskFlow/issues)
