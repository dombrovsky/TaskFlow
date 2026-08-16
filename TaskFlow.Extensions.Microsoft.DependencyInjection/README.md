# TaskFlow.Microsoft.Extensions.DependencyInjection

`TaskFlow.Microsoft.Extensions.DependencyInjection` integrates TaskFlow with the Microsoft dependency-injection container. It provides scoped schedulers, automatic lifetime management, named configurations, and factories for explicitly owned flows.

## Installation

```shell
dotnet add package TaskFlow.Microsoft.Extensions.DependencyInjection
```

This package references the core `TaskFlow` package.

## Basic usage

Register a scoped TaskFlow execution lane:

```csharp
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks.Flow;

services.AddTaskFlow();
```

Inject `ITaskScheduler` into a scoped service and enqueue work through it:

```csharp
public sealed class ReportWriter
{
    private readonly ITaskScheduler _scheduler;
    private readonly IReportStore _store;

    public ReportWriter(ITaskScheduler scheduler, IReportStore store)
    {
        _scheduler = scheduler;
        _store = store;
    }

    public Task SaveAsync(
        Report report,
        CancellationToken cancellationToken = default)
    {
        return _scheduler.Enqueue(
            token => _store.SaveAsync(report, token),
            cancellationToken);
    }
}
```

The container creates one TaskFlow for each dependency-injection scope and disposes it when that scope ends.

## Named flows and factories

Register named options when different consumers need different flow configurations:

```csharp
services.AddTaskFlow();
services.AddTaskFlow(
    "imports",
    new TaskFlowOptions
    {
        SynchronousDisposeTimeout = TimeSpan.FromSeconds(30)
    });
```

Create an explicitly owned named flow through `ITaskFlowFactory`:

```csharp
await using ITaskFlow importFlow = factory.CreateTaskFlow("imports");
await importFlow.Enqueue(token => ImportAsync(token));
```

The advanced `AddTaskFlow` overload can also provide a custom base-flow factory, dynamically resolved options, and a scheduler-decorator chain.

## Lifetime notes

- `ITaskScheduler` and `ITaskFlowInfo` are registered as scoped services.
- `ITaskFlowFactory` and the default factory are registered as singletons.
- `ITaskFlow` is not registered directly. Use the scoped scheduler for container-owned work or `ITaskFlowFactory` when the caller needs to own and dispose a flow.
- Do not dispose an injected scoped scheduler; the dependency-injection scope owns its underlying TaskFlow.

## Links

- [TaskFlow repository](https://github.com/dombrovsky/TaskFlow)
- [Dependency-injection source](https://github.com/dombrovsky/TaskFlow/tree/main/TaskFlow.Extensions.Microsoft.DependencyInjection)
- [Core TaskFlow package documentation](https://github.com/dombrovsky/TaskFlow/blob/main/TaskFlow/README.md)
- [License](https://github.com/dombrovsky/TaskFlow/blob/main/LICENSE)
- [Issues and feedback](https://github.com/dombrovsky/TaskFlow/issues)
