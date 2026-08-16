# TaskFlow.Extensions.Time

`TaskFlow.Extensions.Time` supplies `WithThrottle` to consumers using TaskFlow's `netstandard2.0` asset through `Microsoft.Bcl.TimeProvider`. .NET 8 and .NET 10 consumers receive the same API directly from the core `TaskFlow` package.

## Install

```shell
dotnet add package TaskFlow.Extensions.Time
```

## Leading-edge admission throttling

```csharp
using System.Threading.Tasks.Flow;

await using var flow = new TaskFlow();
ITaskScheduler throttled = flow.WithThrottle(TimeSpan.FromSeconds(1));

await throttled.Enqueue(token => SendUpdateAsync(token));

try
{
    await throttled.Enqueue(token => SendUpdateAsync(token));
}
catch (OperationThrottledException)
{
    // Rejected inside the one-second admission interval.
}
```

The first submission is admitted immediately. Later submissions inside the interval fail with `OperationThrottledException` without reaching the wrapped scheduler. Admission time is recorded before execution, so accepted operations consume the interval even if they later fail or observe cancellation.

This is leading-edge throttling, not trailing-edge debounce: rejected work is not delayed, queued, or replaced. Pass a custom `TimeProvider` for deterministic tests.

The returned scheduler is a decorator and does not own the underlying flow.

## Documentation

- [Reliability extensions](https://dombrovsky.github.io/TaskFlow/extensions/reliability/)
- [Semantics and pitfalls](https://dombrovsky.github.io/TaskFlow/semantics-and-pitfalls/)
- [Compatibility](https://dombrovsky.github.io/TaskFlow/compatibility/)

Source, license, and feedback are available in the [TaskFlow repository](https://github.com/dombrovsky/TaskFlow).
