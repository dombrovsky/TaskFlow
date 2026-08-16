# TaskFlow.Microsoft.Extensions.Logging

This package adds structured `Microsoft.Extensions.Logging` lifecycle events to any TaskFlow `ITaskScheduler`.

## Install

```shell
dotnet add package TaskFlow.Microsoft.Extensions.Logging
```

## Log operation lifecycles

```csharp
using Microsoft.Extensions.Logging;
using System.Threading.Tasks.Flow;

await using var flow = new TaskFlow();

ITaskScheduler operations = flow
    .WithLogging(logger, options =>
    {
        options.StartedLogLevel = LogLevel.Information;
        options.SucceededLogLevel = LogLevel.Information;
        options.FailedLogLevel = LogLevel.Error;
    })
    .WithOperationName("orders.persist");

await operations.Enqueue(token => PersistOrdersAsync(token));
```

The decorator can emit enqueued, started, cancellation-requested, succeeded or failed, and finished events. Events include structured operation IDs, optional operation names, durations where applicable, and failure exceptions.

Place `WithOperationName` outside `WithLogging`, as shown, so logging can read the annotation. Cancellation logging reports a request rather than the final outcome. Failures remain observable through the task returned by `Enqueue`.

The logging decorator does not own the underlying flow.

## Documentation

- [Observability extensions](https://dombrovsky.github.io/TaskFlow/extensions/observability/)
- [Extension composition](https://dombrovsky.github.io/TaskFlow/extensions/)
- [Semantics and pitfalls](https://dombrovsky.github.io/TaskFlow/semantics-and-pitfalls/)

Source, license, and feedback are available in the [TaskFlow repository](https://github.com/dombrovsky/TaskFlow).
