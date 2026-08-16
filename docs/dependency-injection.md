---
layout: page
title: Dependency injection
permalink: /dependency-injection/
---

# Dependency injection

Install the integration package:

```shell
dotnet add package TaskFlow.Microsoft.Extensions.DependencyInjection
```

The package targets `netstandard2.0`, .NET 8, and .NET 10 and uses the `System.Threading.Tasks.Flow` namespace.

## Scoped execution lane

```csharp
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks.Flow;

var services = new ServiceCollection();
services.AddTaskFlow();
services.AddScoped<ReportWriter>();

await using ServiceProvider provider = services.BuildServiceProvider();
await using AsyncServiceScope scope = provider.CreateAsyncScope();

var writer = scope.ServiceProvider.GetRequiredService<ReportWriter>();
await writer.SaveAsync(new Report());

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

public sealed record Report;
```

`AddTaskFlow()` registers one scoped scheduler and flow information object. The dependency-injection scope owns the underlying flow and disposes it when the scope ends. Consumers should inject `ITaskScheduler` and must not dispose it.

## Options

Provide options when a scope needs a different .NET scheduler or synchronous-disposal timeout:

```csharp
services.AddTaskFlow(new TaskFlowOptions
{
    TaskScheduler = TaskScheduler.Default,
    SynchronousDisposeTimeout = TimeSpan.FromSeconds(15),
});
```

Prefer asynchronous scope disposal. A finite synchronous timeout can return before noncooperative work finishes.

## Named flows

Named registrations hold independent configurations:

```csharp
services.AddTaskFlow();
services.AddTaskFlow(
    "imports",
    new TaskFlowOptions
    {
        SynchronousDisposeTimeout = TimeSpan.FromSeconds(30),
    });
```

A named flow is still sequential. Names select configurations; they do not create concurrency within a flow.

Use `ITaskFlowFactory.CreateTaskFlow(name)` when the caller needs an explicitly owned named flow:

```csharp
await using ITaskFlow imports = factory.CreateTaskFlow("imports");
await imports.Enqueue(token => ImportAsync(token));

static Task ImportAsync(CancellationToken token) => Task.CompletedTask;
```

The returned `ITaskFlow` belongs to the caller and must be disposed.

## Advanced registration

The advanced `AddTaskFlow` overload accepts delegates for:

- creating the underlying `ITaskFlow`;
- resolving named `TaskFlowOptions`; and
- composing an `ITaskScheduler` decorator chain.

Use it to centralize cancellation, timeout, logging, or application-specific wrappers. Preserve the root flow separately from the decorated scheduler so the dependency-injection scope disposes the actual owner.

## Registered lifetimes

- `ITaskScheduler` and `ITaskFlowInfo` are scoped.
- `ITaskFlowFactory` and the default factory are singletons.
- `ITaskFlow` is not registered directly for consumers.
- Factory-created flows are caller-owned; injected scoped schedulers are container-owned.
