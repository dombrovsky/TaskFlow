---
layout: page
title: Reliability extensions
permalink: /extensions/reliability/
---

# Reliability extensions

Timeout and throttle run in the enqueue phase, so timeout still includes queue waiting and throttle still rejects before a terminal queue turn is consumed. Failures produced before scheduled execution pass through completion middleware once, but no terminal thread or synchronization-context affinity can be promised for those admission failures.

## Timeout

`WithTimeout` applies one budget to queue waiting and delegate execution. It throws `TimeoutException` when the timer wins and requests cancellation of the underlying operation.

```csharp
await using var flow = new TaskFlow();
ITaskScheduler bounded = flow
    .WithTimeout(TimeSpan.FromSeconds(2))
    .WithOperationName("catalog.refresh");

try
{
    await bounded.Enqueue(token => RefreshAsync(token));
}
catch (TimeoutException exception)
{
    Console.Error.WriteLine(exception.Message);
}

static Task RefreshAsync(CancellationToken token) => Task.CompletedTask;
```

The clock starts when `Enqueue` reaches the timeout wrapper, not when the delegate starts. On a busy built-in flow, the returned task can time out while waiting. The queued delegate is then invoked later with an already-canceled token. If it ignores cancellation, it can continue and hold the lane even though the caller already received `TimeoutException`.

## Error observation

`OnError<TException>` runs an action for matching exceptions and then rethrows so the returned task preserves failure.

```csharp
await using var flow = new TaskFlow();
ITaskScheduler observed = flow.OnError<IOException>(exception =>
    Console.Error.WriteLine(exception.Message));

try
{
    await observed.Enqueue(token => FailAsync(token));
}
catch (IOException)
{
    // The caller still receives the operation failure.
}

static Task FailAsync(CancellationToken token) =>
    Task.FromException(new IOException("Storage unavailable"));
```

An optional filter can restrict which matching exceptions trigger the action. If the action itself throws, its exception replaces the operation exception. `OnError` does not suppress failures, retry work, or keep a failed background loop alive.

## Leading-edge throttle

`WithThrottle` admits the first submission immediately and rejects later submissions until the interval has elapsed.

```csharp
await using var flow = new TaskFlow();
ITaskScheduler throttled =
    flow.WithThrottle(TimeSpan.FromSeconds(1));

await throttled.Enqueue(token => SendAsync(token));

try
{
    await throttled.Enqueue(token => SendAsync(token));
}
catch (OperationThrottledException)
{
    // Rejected before reaching the underlying flow.
}

static Task SendAsync(CancellationToken token) => Task.CompletedTask;
```

Admission is checked when the wrapper receives `Enqueue`. Accepted operations consume the interval even if they later fail or observe cancellation. An operation at the exact interval boundary is admitted. Rejected work is not delayed, queued, or replaced.

`OperationThrottledException` derives from `OperationCanceledException`, so catch it first when the application needs to distinguish rejection from other cancellation.

On .NET 8 and .NET 10, `WithThrottle` is in the core `TaskFlow` package. When a consumer resolves TaskFlow's `netstandard2.0` asset, install `TaskFlow.Extensions.Time`, which supplies the same API using `Microsoft.Bcl.TimeProvider`.

## Choosing a policy

| Need | Use |
|---|---|
| Bound queue wait plus execution time | `WithTimeout` |
| Report failures without changing successful behavior | `OnError` |
| Admit at most one submission per interval | `WithThrottle` |
| Cancel older unfinished operations | `CreateCancelPrevious` |
| Retry or suppress failures | Implement inside the delegate or use an external policy; TaskFlow has no built-in retry policy |
