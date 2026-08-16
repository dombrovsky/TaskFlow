# TaskFlow.Microsoft.Extensions.Logging

`TaskFlow.Microsoft.Extensions.Logging` adds structured lifecycle logging to any TaskFlow `ITaskScheduler` through `Microsoft.Extensions.Logging`.

## Installation

```shell
dotnet add package TaskFlow.Microsoft.Extensions.Logging
```

This package references the core `TaskFlow` package.

## Basic usage

```csharp
using Microsoft.Extensions.Logging;
using System.Threading.Tasks.Flow;

await using var flow = new TaskFlow();

ITaskScheduler operations = flow
    .WithLogging(logger, options =>
    {
        options.EnqueuedLogLevel = LogLevel.Debug;
        options.StartedLogLevel = LogLevel.Information;
        options.SucceededLogLevel = LogLevel.Information;
        options.FailedLogLevel = LogLevel.Error;
        options.FinishedLogLevel = LogLevel.Debug;
    })
    .WithOperationName("orders.persist");

await operations.Enqueue(token => PersistOrdersAsync(token));
```

Place `WithOperationName` outside `WithLogging`, as shown above, so the logging decorator can read the operation-name annotation.

## Logged lifecycle

The decorator can emit an event when an operation is:

- enqueued;
- starting;
- requested to cancel through its enqueue token;
- completed successfully;
- failed or observed cancellation; and
- finished, regardless of outcome.

All event levels default to `LogLevel.Trace`. Set any corresponding `TaskFlowLoggingOptions` property to `LogLevel.None` to disable that event.

## Behavior and lifetime

- The cancellation-request event reports a request, not the final outcome. The operation can still complete successfully if it does not observe cancellation.
- Failures are logged with their exception and still propagate through the task returned by `Enqueue`.
- Each logging wrapper assigns increasing operation IDs to the operations it observes.
- `WithLogging` returns a scheduler decorator and does not own the underlying flow. Dispose the original `ITaskFlow`.
- Decorator order is observable; add operation names and other annotations outside the logging wrapper when the logger should include them.

## Links

- [TaskFlow repository](https://github.com/dombrovsky/TaskFlow)
- [Logging extension source](https://github.com/dombrovsky/TaskFlow/tree/main/TaskFlow.Extensions.Microsoft.Logging)
- [Core TaskFlow package documentation](https://github.com/dombrovsky/TaskFlow/blob/main/TaskFlow/README.md)
- [License](https://github.com/dombrovsky/TaskFlow/blob/main/LICENSE)
- [Issues and feedback](https://github.com/dombrovsky/TaskFlow/issues)
