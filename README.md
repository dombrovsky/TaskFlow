# TaskFlow for .NET

TaskFlow turns calls from many places into one owned FIFO lane of work. Every submission gets an awaitable result while the lane serializes execution and provides a clear lifetime boundary.

[![NuGet](https://img.shields.io/nuget/v/TaskFlow.svg)](https://www.nuget.org/packages/TaskFlow/)
[![Build](https://github.com/dombrovsky/TaskFlow/actions/workflows/build.yml/badge.svg)](https://github.com/dombrovsky/TaskFlow/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

## Why TaskFlow?

Applications often need to accept work asynchronously while ensuring that only one operation touches a resource at a time and that operations retain their original order. Building that around a semaphore or task chain leaves ordering, per-call completion, cancellation, error observation, and shutdown ownership in application code.

TaskFlow packages those concerns into a reusable execution lane. It is useful when you need to:

- expose an asynchronous API over a synchronous or non-thread-safe resource;
- preserve event order when synchronous callbacks initiate work;
- own background work within a component or dependency-injection scope;
- cancel obsolete operations when a newer request arrives;
- add timeouts, throttling, logging, annotations, or error observation without changing the work itself; or
- run ordered work on the thread pool, a dedicated thread, a caller-owned thread, or a custom scheduler.

One flow is one sequential lane. Create separate flows for work that should proceed independently.

## Features

| Feature | What it provides |
|---|---|
| FIFO execution | Accepted operations start in submission order and do not overlap within one flow. |
| Per-operation tasks | Every caller can await its own result, exception, or cancellation. |
| Owned lifetime | A flow gives queued and running work an explicit component-level shutdown boundary. |
| Composable policies | Add cancellation scopes, latest-request-wins behavior, timeouts, leading-edge throttling, operation names, interception, and error observation. |
| Execution choices | Use the thread pool, a dedicated thread, the current thread, or another `TaskScheduler`. |
| Application integration | Register scoped or named flows and emit structured lifecycle logs. |
| Extensibility | Build scheduler decorators, adapters, interceptors, or custom `TaskFlowBase` implementations. |

See the [extension reference](https://dombrovsky.github.io/TaskFlow/extensions/) and [execution models](https://dombrovsky.github.io/TaskFlow/execution-models/) for the available policies and implementations.

## Serialize synchronous work for asynchronous callers

```csharp
using System.Threading.Tasks.Flow;

public interface IDataStore
{
    void Save(Data data);
}

public sealed class SerializedStore(IDataStore inner) : IAsyncDisposable
{
    private readonly TaskFlow _flow = new();

    public Task SaveAsync(Data data) =>
        _flow.Enqueue(() => inner.Save(data));

    public ValueTask DisposeAsync() => _flow.DisposeAsync();
}
```

Callers receive a task instead of blocking on `Save`. The wrapped synchronous method runs once at a time and in call order, regardless of how many callers submit work concurrently.

## Install

```shell
dotnet add package TaskFlow
```

Optional dependency-injection, logging, and time integrations are available from the [TaskFlow packages on NuGet](https://www.nuget.org/profiles/dombrovsky).

## Lifecycle essentials

- Observe every task returned by `Enqueue`; ownership does not make failures unobservable.
- Prefer `await using` so asynchronous disposal can wait for the lane to finish.
- Cancellation is cooperative, and synchronous disposal has a timeout.
- Scheduler decorators do not own the underlying flow; dispose the original `ITaskFlow`.

Read [Concepts and lifecycle](https://dombrovsky.github.io/TaskFlow/concepts-and-lifecycle/) and [Semantics and pitfalls](https://dombrovsky.github.io/TaskFlow/semantics-and-pitfalls/) for the full behavior contract.

## Documentation

- [Getting started](https://dombrovsky.github.io/TaskFlow/getting-started/)
- [Recipes](https://dombrovsky.github.io/TaskFlow/recipes/)
- [Extensions](https://dombrovsky.github.io/TaskFlow/extensions/)
- [Dependency injection](https://dombrovsky.github.io/TaskFlow/dependency-injection/)
- [Troubleshooting](https://dombrovsky.github.io/TaskFlow/troubleshooting/)

TaskFlow is available under the [MIT License](LICENSE). Contributions and problem reports are welcome through [GitHub issues](https://github.com/dombrovsky/TaskFlow/issues).
