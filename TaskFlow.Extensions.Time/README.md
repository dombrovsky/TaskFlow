# TaskFlow.Extensions.Time

`TaskFlow.Extensions.Time` is only needed on older .NET runtimes when you want to use time-based extension methods such as `WithThrottle`; on newer runtimes, these extensions are included directly in the core `TaskFlow` package.

`WithThrottle` provides leading-edge throttling: it accepts the first operation and rejects later operations submitted during the configured interval.

## Installation

```shell
dotnet add package TaskFlow.Extensions.Time
```

This package references the core `TaskFlow` package and uses `Microsoft.Bcl.TimeProvider`.

## Basic usage

```csharp
using System.Threading.Tasks.Flow;

await using var flow = new TaskFlow();
ITaskScheduler throttled = flow.WithThrottle(TimeSpan.FromSeconds(1));

await throttled.Enqueue(() => SendUpdateAsync());

try
{
    await throttled.Enqueue(() => SendUpdateAsync());
}
catch (OperationThrottledException)
{
    // The second operation was submitted inside the one-second interval.
}
```

After the interval elapses, the next submitted operation is accepted and starts a new interval.

## Behavior

- The first operation is accepted immediately.
- An operation submitted before the interval elapses fails with `OperationThrottledException` and is not forwarded to the wrapped scheduler.
- The interval is checked when `Enqueue` is called, so time spent waiting in the wrapped scheduler does not delay the start of the interval.
- The accepted timestamp is recorded before the operation executes. An accepted operation that later fails or observes cancellation still consumes the interval.
- This is leading-edge throttling, not trailing-edge debouncing: rejected work is not delayed, queued, or replaced with the latest request.
- Pass a custom `TimeProvider` to `WithThrottle` for deterministic time control in tests.
- The returned scheduler is a decorator and does not own the underlying flow. Dispose the original `ITaskFlow`.

## Links

- [TaskFlow repository](https://github.com/dombrovsky/TaskFlow)
- [Time extension source](https://github.com/dombrovsky/TaskFlow/tree/main/TaskFlow.Extensions.Time)
- [Core TaskFlow package documentation](https://github.com/dombrovsky/TaskFlow/blob/main/TaskFlow/README.md)
- [License](https://github.com/dombrovsky/TaskFlow/blob/main/LICENSE)
- [Issues and feedback](https://github.com/dombrovsky/TaskFlow/issues)
