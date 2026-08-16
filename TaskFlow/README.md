# TaskFlow for .NET

`TaskFlow` provides owned FIFO execution lanes for asynchronous .NET work. Use it when operations must run one at a time in submission order while each caller retains a task for its own result, failure, or cancellation.

## Install

```shell
dotnet add package TaskFlow
```

## Basic usage

```csharp
using System.Threading.Tasks.Flow;

await using var flow = new TaskFlow();

Task first = flow.Enqueue(token => SaveAsync("first", token));
Task second = flow.Enqueue(token => SaveAsync("second", token));

await Task.WhenAll(first, second);
```

`second` starts only after `first` finishes. Calls to `Enqueue` are thread-safe, and one failed operation does not stop later queued operations.

## Important behavior

- Cancellation is cooperative. Built-in flows invoke an accepted queued delegate even if its token was canceled while waiting.
- `DisposeAsync` requests cancellation and waits for the lane to finish. Synchronous disposal is bounded by `TaskFlowOptions.SynchronousDisposeTimeout`.
- Observe every returned operation task; disposal does not surface individual operation failures.
- Timeout includes time spent waiting in the underlying queue.
- Scheduler decorators do not own the flow they wrap. Dispose the original `ITaskFlow`.

TaskFlow also provides dedicated-thread and caller-owned-thread flows, cancellation and timeout policies, leading-edge throttling, error observation, annotations, and interception.

## Documentation

- [Getting started](https://dombrovsky.github.io/TaskFlow/getting-started/)
- [Concepts and lifecycle](https://dombrovsky.github.io/TaskFlow/concepts-and-lifecycle/)
- [Semantics and pitfalls](https://dombrovsky.github.io/TaskFlow/semantics-and-pitfalls/)
- [Execution models](https://dombrovsky.github.io/TaskFlow/execution-models/)
- [Recipes](https://dombrovsky.github.io/TaskFlow/recipes/)
- [Extensions](https://dombrovsky.github.io/TaskFlow/extensions/)
- [Compatibility](https://dombrovsky.github.io/TaskFlow/compatibility/)
- [Troubleshooting](https://dombrovsky.github.io/TaskFlow/troubleshooting/)

Source, license, and feedback are available in the [TaskFlow repository](https://github.com/dombrovsky/TaskFlow).
