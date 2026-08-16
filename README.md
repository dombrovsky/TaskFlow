# TaskFlow for .NET

TaskFlow provides owned FIFO execution lanes for asynchronous .NET work, with composable cancellation, timeout, diagnostics, and thread-affinity policies.

[![NuGet](https://img.shields.io/nuget/v/TaskFlow.svg)](https://www.nuget.org/packages/TaskFlow/)
[![Build](https://github.com/dombrovsky/TaskFlow/actions/workflows/build.yml/badge.svg)](https://github.com/dombrovsky/TaskFlow/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Use TaskFlow to:

- serialize asynchronous access to a mutable or non-thread-safe resource;
- preserve event order when synchronous callbacks start asynchronous work;
- bind background work to a component or dependency-injection scope;
- cancel obsolete work when a newer request arrives;
- compose cancellation, timeout, logging, annotations, and error observation; and
- run work on the thread pool, a dedicated thread, or a caller-owned thread.

## Install

```shell
dotnet add package TaskFlow
```

Optional integrations:

```shell
dotnet add package TaskFlow.Microsoft.Extensions.DependencyInjection
dotnet add package TaskFlow.Microsoft.Extensions.Logging
dotnet add package TaskFlow.Extensions.Time
```

`TaskFlow.Extensions.Time` is needed only by consumers that resolve TaskFlow's `netstandard2.0` asset and use `WithThrottle`; .NET 8 and .NET 10 receive that extension from the core package.

## A FIFO execution lane

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

`second` starts only after `first` finishes. Each call returns a task for that operation's result, exception, or cancellation. One failed operation does not stop later queued operations.

## Serialize a resource

```csharp
public sealed class SerializedStore : IAsyncDisposable
{
    private readonly IDataStore _inner;
    private readonly TaskFlow _flow = new();

    public SerializedStore(IDataStore inner)
    {
        _inner = inner;
    }

    public Task SaveAsync(
        Data data,
        CancellationToken cancellationToken = default)
    {
        return _flow.Enqueue(
            token => _inner.SaveAsync(data, token),
            cancellationToken);
    }

    public ValueTask DisposeAsync() => _flow.DisposeAsync();
}
```

Callers remain asynchronous while access to the wrapped resource stays ordered and non-concurrent.

## Latest request wins

```csharp
await using var flow = new TaskFlow();
ITaskScheduler latest = flow.CreateCancelPrevious();

Task search = latest.Enqueue(async token =>
{
    await Task.Delay(TimeSpan.FromMilliseconds(250), token);
    await SearchAsync(token);
});

await search;
```

Every new submission requests cancellation of older unfinished work. The delay creates a latest-request-wins pattern when delegates cooperate with cancellation.

## Features

| Capability | API or implementation | Documentation |
|---|---|---|
| FIFO asynchronous execution | `TaskFlow` | [Concepts and lifecycle](https://dombrovsky.github.io/TaskFlow/concepts-and-lifecycle/) |
| Thread affinity | `DedicatedThreadTaskFlow`, `CurrentThreadTaskFlow` | [Execution models](https://dombrovsky.github.io/TaskFlow/execution-models/) |
| Latest request wins | `CreateCancelPrevious` | [Cancellation](https://dombrovsky.github.io/TaskFlow/extensions/cancellation/) |
| Component cancellation | `CreateCancellationScope` | [Cancellation](https://dombrovsky.github.io/TaskFlow/extensions/cancellation/) |
| Queue-and-execution timeout | `WithTimeout` | [Reliability](https://dombrovsky.github.io/TaskFlow/extensions/reliability/) |
| Leading-edge admission throttle | `WithThrottle` | [Reliability](https://dombrovsky.github.io/TaskFlow/extensions/reliability/) |
| Error observation | `OnError` | [Reliability](https://dombrovsky.github.io/TaskFlow/extensions/reliability/) |
| Structured lifecycle logging | `WithLogging` | [Observability](https://dombrovsky.github.io/TaskFlow/extensions/observability/) |
| Scoped and named registration | DI integration package | [Dependency injection](https://dombrovsky.github.io/TaskFlow/dependency-injection/) |

## When to use something else

| Primitive | Prefer it when |
|---|---|
| `lock` | The entire critical section is synchronous. |
| `SemaphoreSlim` | Mutual exclusion is enough and callers will manage acquisition, release, ordering, and lifetime. |
| `Channel<T>` | The application is fundamentally a producer/consumer data stream. |
| `BackgroundService` | Work belongs to the application host lifetime rather than a smaller component. |
| TaskFlow | Each submission needs FIFO execution, its own result task, composable policies, and an owned shutdown boundary. |

## Packages and frameworks

| Package | Frameworks |
|---|---|
| `TaskFlow` | `netstandard2.0`, `net8.0`, `net10.0` |
| `TaskFlow.Extensions.Time` | `netstandard2.0` |
| `TaskFlow.Microsoft.Extensions.DependencyInjection` | `netstandard2.0`, `net8.0`, `net10.0` |
| `TaskFlow.Microsoft.Extensions.Logging` | `netstandard2.0`, `net8.0`, `net10.0` |

See the [compatibility matrix](https://dombrovsky.github.io/TaskFlow/compatibility/) for feature-level availability.

## Lifecycle essentials

- Prefer `await using`; `DisposeAsync` requests cancellation and waits for the lane to finish.
- Cancellation is cooperative. Synchronous disposal can time out while noncooperative work continues.
- Observe every task returned by `Enqueue`, even when the flow owns the work's lifetime.
- Built-in flows invoke accepted queued delegates with canceled tokens instead of removing them from the lane.
- Scheduler decorators do not own the underlying flow; dispose the original `ITaskFlow`.

Read [Semantics and pitfalls](https://dombrovsky.github.io/TaskFlow/semantics-and-pitfalls/) before using timeouts or owning long-running background work.

## Documentation

- [Documentation home](https://dombrovsky.github.io/TaskFlow/)
- [Getting started](https://dombrovsky.github.io/TaskFlow/getting-started/)
- [Recipes](https://dombrovsky.github.io/TaskFlow/recipes/)
- [Extensions](https://dombrovsky.github.io/TaskFlow/extensions/)
- [Troubleshooting](https://dombrovsky.github.io/TaskFlow/troubleshooting/)

Build the repository with a .NET 10 SDK. Tests run against .NET 8 and .NET 10.

TaskFlow is available under the [MIT License](LICENSE). Contributions and problem reports are welcome through [GitHub issues](https://github.com/dombrovsky/TaskFlow/issues).
