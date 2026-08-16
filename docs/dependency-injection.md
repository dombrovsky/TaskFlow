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

## Compose decorators at registration

The advanced `AddTaskFlow` overload accepts `configureSchedulerChain`, which moves scheduler policy into the application's composition root. Consumers receive the configured `ITaskScheduler`; they do not need to construct, order, or retain references to its decorators.

This is especially useful when a named TaskFlow configuration is exposed as a keyed DI service:

```csharp
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks.Flow;

services.AddTaskFlow();

services.AddTaskFlow(
    name: "imports",
    configureSchedulerChain: (scheduler, _) => scheduler
        .WithOperationName("imports")
        .WithTimeout(TimeSpan.FromSeconds(30)));

services.AddKeyedScoped<ITaskScheduler>(
    "imports",
    (provider, _) => provider
        .GetRequiredService<ITaskFlowFactory>()
        .CreateTaskFlow("imports"));

public sealed class ImportWorker(
    [FromKeyedServices("imports")] ITaskScheduler scheduler)
{
    public Task RunAsync(CancellationToken cancellationToken) =>
        scheduler.Enqueue(ImportAsync, cancellationToken);

    private static Task ImportAsync(CancellationToken token) =>
        Task.CompletedTask;
}
```

The consumer knows only the service key and `ITaskScheduler`. The registration owns the operation name and timeout policies, so they can change without changing `ImportWorker`.

`AddTaskFlow("imports", ...)` defines the named TaskFlow configuration; it does not by itself register a keyed `ITaskScheduler`. The explicit scoped bridge above asks `ITaskFlowFactory` to create the named flow. Because the DI container creates that scoped service, it also disposes the returned `ITaskFlow` ownership wrapper at the end of the scope.

The same pipeline mechanism works for the ordinary unkeyed scoped scheduler by passing `name: null`. A chain can resolve services from the provided `IServiceProvider` and can compose cancellation, timeout, logging, interception, or application-specific wrappers.

Decorator order is observable: each extension wraps the scheduler returned by the previous call, so the last extension is the outermost decorator. See [Semantics and pitfalls](semantics-and-pitfalls.md#decorator-order-changes-what-a-policy-sees).

The factory keeps the root `ITaskFlow` separate from the decorated scheduler. Disposing a factory-created flow or a container-owned scoped registration therefore disposes the real owner rather than relying on its decorators to own it.

## Advanced creation

The same `AddTaskFlow` overload can also customize creation of the underlying `ITaskFlow` and resolve `TaskFlowOptions` from the service provider. Use those delegates when a named configuration needs a different execution model or options determined from other registered services.

## Registered lifetimes

- `ITaskScheduler` and `ITaskFlowInfo` are scoped.
- `ITaskFlowFactory` and the default factory are singletons.
- `ITaskFlow` is not registered directly for consumers.
- Factory-created flows are caller-owned; injected scoped schedulers are container-owned.
