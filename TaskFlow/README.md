# TaskFlow

`TaskFlow` provides an owned FIFO execution lane for asynchronous .NET work. Use it when operations must run one at a time, in submission order, while each caller still receives a task for its own result or failure.

## Installation

```shell
dotnet add package TaskFlow
```

## Basic usage

```csharp
using System.Threading.Tasks.Flow;

await using var flow = new TaskFlow();

Task first = flow.Enqueue(async cancellationToken =>
{
    await SaveAsync("first", cancellationToken);
});

Task second = flow.Enqueue(async cancellationToken =>
{
    await SaveAsync("second", cancellationToken);
});

await Task.WhenAll(first, second);
```

`second` does not begin until `first` finishes. Calls to `Enqueue` are thread-safe, so multiple callers can share the same flow to serialize access to a resource.

## Behavior and lifetime

- Operations execute sequentially in FIFO order.
- Each returned task reports the result, cancellation, or exception of its own operation. One failed operation does not stop later queued operations from running.
- Cancellation is cooperative. A queued delegate is still invoked when its token has already been canceled, allowing queue progression while giving the delegate the canceled token.
- `DisposeAsync` requests cancellation and waits for queued work to finish. Synchronous disposal waits up to `TaskFlowOptions.SynchronousDisposeTimeout`.
- Scheduler decorators such as timeout, cancellation-scope, error-observation, interception, and cancel-previous wrappers do not own the underlying flow. Dispose the `ITaskFlow` that created the execution lane.

TaskFlow also includes `DedicatedThreadTaskFlow` and `CurrentThreadTaskFlow` for work that requires thread affinity.

## Links

- [TaskFlow repository](https://github.com/dombrovsky/TaskFlow)
- [Source code](https://github.com/dombrovsky/TaskFlow/tree/main/TaskFlow)
- [License](https://github.com/dombrovsky/TaskFlow/blob/main/LICENSE)
- [Issues and feedback](https://github.com/dombrovsky/TaskFlow/issues)
