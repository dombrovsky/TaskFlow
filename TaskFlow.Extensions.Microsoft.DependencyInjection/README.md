# TaskFlow.Microsoft.Extensions.DependencyInjection

This package integrates TaskFlow with `Microsoft.Extensions.DependencyInjection`, providing scoped FIFO execution lanes, named configurations, decorator chains, and factories for explicitly owned flows.

## Install

```shell
dotnet add package TaskFlow.Microsoft.Extensions.DependencyInjection
```

## Scoped flow

```csharp
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks.Flow;

services.AddTaskFlow();
```

Inject `ITaskScheduler` into scoped consumers:

```csharp
public sealed class ReportWriter
{
    private readonly ITaskScheduler _scheduler;

    public ReportWriter(ITaskScheduler scheduler)
    {
        _scheduler = scheduler;
    }

    public Task SaveAsync(
        Report report,
        CancellationToken cancellationToken = default)
    {
        return _scheduler.Enqueue(
            token => PersistAsync(report, token),
            cancellationToken);
    }

    private static Task PersistAsync(
        Report report,
        CancellationToken token) => Task.CompletedTask;
}
```

The scope owns and disposes its underlying flow. Do not dispose an injected scheduler.

Named registrations select independently configured sequential flows. When the caller needs ownership, create an `ITaskFlow` through `ITaskFlowFactory` and dispose it with `await using`.

## Documentation

- [Dependency injection guide](https://dombrovsky.github.io/TaskFlow/dependency-injection/)
- [Concepts and lifecycle](https://dombrovsky.github.io/TaskFlow/concepts-and-lifecycle/)
- [Extension composition](https://dombrovsky.github.io/TaskFlow/extensions/)

Source, license, and feedback are available in the [TaskFlow repository](https://github.com/dombrovsky/TaskFlow).
