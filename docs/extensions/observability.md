---
layout: page
title: Observability extensions
permalink: /extensions/observability/
---

# Observability extensions

## Operation names

`WithOperationName` attaches an `OperationNameAnnotation` to submissions. Place it outside consumers such as logging or timeout so they can read the annotation.

```csharp
ITaskScheduler named = flow
    .WithLogging(logger)
    .WithOperationName("orders.persist");

await named.Enqueue(token => PersistAsync(token));

static Task PersistAsync(CancellationToken token) => Task.CompletedTask;
```

Decorator order is observable: `flow.WithOperationName(...).WithLogging(logger)` places logging outside the annotation and therefore does not provide that name to the logging wrapper.

## Microsoft logging

Install the integration package:

```shell
dotnet add package TaskFlow.Microsoft.Extensions.Logging
```

```csharp
ITaskScheduler logged = flow
    .WithLogging(logger, options =>
    {
        options.EnqueuedLogLevel = LogLevel.Debug;
        options.StartedLogLevel = LogLevel.Information;
        options.SucceededLogLevel = LogLevel.Information;
        options.FailedLogLevel = LogLevel.Error;
        options.FinishedLogLevel = LogLevel.Debug;
    })
    .WithOperationName("imports.run");

await logged.Enqueue(token => ImportAsync(token));

static Task ImportAsync(CancellationToken token) => Task.CompletedTask;
```

The decorator can emit enqueued, started, cancellation-requested, succeeded or failed, and finished events. Every level defaults to `Trace`; set a level to `LogLevel.None` to disable that event. Events include an increasing operation ID, optional name, duration where applicable, and the failure exception.

The cancellation event reports a request, not the final outcome. A delegate can ignore cancellation and complete successfully. Logging never suppresses the operation exception.

## Interception

`Intercept` supports custom operation lifecycle behavior. A synchronous interceptor is a struct copied for each operation and implements callbacks in this order:

1. `OnBefore`;
2. the operation;
3. either `OnSuccess` or `OnError`; and
4. `OnFinally`.

An asynchronous interceptor uses `IAsyncTaskSchedulerInterceptor` to create one `IAsyncTaskInterceptor` per operation. Every returned `ValueTask` is awaited before the lifecycle advances.

Callbacks run inside the selected scheduler context. A callback failure faults the returned operation task; error or finalization callback failures can replace the original operation failure. Use interception when this replacement behavior and lifecycle control are intentional. Prefer `OnError` or `WithLogging` for simpler observation.

## Ownership

Annotations, interception, and logging are scheduler decorators. They neither own nor dispose the underlying flow. Keep the original `ITaskFlow` and dispose it at the component boundary.
