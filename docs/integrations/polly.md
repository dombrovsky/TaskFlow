---
layout: page
title: Polly v8 resilience pipelines with TaskFlow
description: Use Polly retry, timeout, and dependency injection inside TaskFlow operations while preserving FIFO and keyed scheduling.
permalink: /integrations/polly/
---

# Polly v8 resilience pipelines with TaskFlow

Run Polly **inside the TaskFlow delegate** to keep retries in the same queue position.

Install `TaskFlow` and `Polly.Core`. The examples use Polly 8.8.0 on .NET 8/10. `SaveAsync(CancellationToken)` returns `ValueTask`; `cancellationToken` comes from the caller.

## Retry an operation

```csharp
using System.Net;
using System.Threading.Tasks.Flow;
using Polly;
using Polly.Retry;

int maxRetries = 3; // Read from application configuration.
bool useJitter = true;
var retryOptions = new RetryStrategyOptions
{
    ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(
        error => error.StatusCode == HttpStatusCode.ServiceUnavailable),
    MaxRetryAttempts = maxRetries,
    Delay = TimeSpan.FromMilliseconds(200),
    BackoffType = DelayBackoffType.Exponential,
    UseJitter = useJitter
};
var pipeline = new ResiliencePipelineBuilder().AddRetry(retryOptions).Build();

await using var flow = new TaskFlow();
await flow.Enqueue(
    scheduledToken => pipeline.ExecuteAsync(SaveAsync, scheduledToken).AsTask(),
    cancellationToken);
```

`MaxRetryAttempts = 3` allows **four calls**: the first attempt plus three retries. Retry only selected transient failures, never cancellation. Make sure repeating the operation is safe.

Pass TaskFlow's token to Polly, then Polly's token to your I/O, as above. Don't use the original caller token inside the operation or retry an existing Task/ValueTask.

Keep `.AsTask()`: without it, TaskFlow can treat a nongeneric `ValueTask` as a result instead of waiting for it. `ValueTask<T>` uses the async result overload.

### Retries and queue order

Retries and delays stay in one queue position. Later operations wait until the pipeline finishes.

Putting Polly around `Enqueue` resubmits the work:

```csharp
await pipeline.ExecuteAsync(
    token => new ValueTask(flow.Enqueue(ct => SaveAsync(ct).AsTask(), token)),
    cancellationToken);
```

Each retry takes a new queue position and checks admission again. Later work may run first. This retries operation failures too, not just scheduling failures. After a TaskFlow timeout, resubmitting can duplicate work that is still running.

## Keyed scheduling

Reuse the pipeline for keyed work. `ReadVersionAsync(string, CancellationToken)` returns `ValueTask<int>`:

```csharp
await using var owner = new TaskFlow();
var scheduler = owner.WithKeyedScheduling<string>().WithConcurrencyLimit(2);
Func<string, CancellationToken, ValueTask<int>> read =
    (key, token) => pipeline.ExecuteAsync(
        attemptToken => ReadVersionAsync(key, attemptToken), token);

Task<int> first = scheduler.Enqueue("document-17", read, cancellationToken);
Task<int> sameKey = scheduler.Enqueue("document-17", read, cancellationToken);
Task<int> otherKey = scheduler.Enqueue("document-18", read, cancellationToken);
await Task.WhenAll(first, sameKey, otherKey);
```

Same-key work waits through all retries. Other keys can run if capacity is free. Retry delays still use an execution slot.

Retries do not create extra admission reservations. See [keyed scheduling](../extensions/keyed-scheduling.md) and [bounded admission](../extensions/admission.md) for setup and capacity rules.

## Timeouts and retries

Add strategies in this order: **total timeout → retry → per-attempt timeout**.

```csharp
using Polly.Timeout;

var execution = new ResiliencePipelineBuilder()
    .AddTimeout(TimeSpan.FromSeconds(12)) // Includes attempts and backoff.
    .AddRetry(new RetryStrategyOptions
    {
        ShouldHandle = new PredicateBuilder()
            .Handle<HttpRequestException>(error =>
                error.StatusCode == HttpStatusCode.ServiceUnavailable)
            .Handle<TimeoutRejectedException>(),
        MaxRetryAttempts = 2,
        Delay = TimeSpan.FromMilliseconds(200)
    })
    .AddTimeout(TimeSpan.FromSeconds(5)) // Fresh budget for each attempt.
    .Build();

var bounded = flow.WithTimeout(TimeSpan.FromSeconds(20));
await bounded.Enqueue(
    token => execution.ExecuteAsync(SaveAsync, token).AsTask(),
    cancellationToken);
```

Handling Polly's `TimeoutRejectedException` lets per-attempt timeouts retry. The total timeout wraps retry, so its budget does not restart. Caller/shutdown cancellation still cancels the run.

TaskFlow's `WithTimeout` throws `System.TimeoutException` and can include queue/admission waits. Polly's timer starts when the operation runs. Put TaskFlow's timeout outside admission to include that wait. See [TaskFlow timeouts](../extensions/reliability.md#timeout).

Timeouts request cancellation; they cannot force code to stop. Polly waits for the callback and may return success if it ignores cancellation. TaskFlow may return a timeout earlier, while work still holds its lane/key, execution slot, and outstanding admission reservation.

## Dedicated threads

Polly does not capture the context by default. For dedicated-thread work, enable it inside the operation:

```csharp
await using var device = new DedicatedThreadTaskFlow("device-io");
await device.Enqueue(async scheduledToken =>
{
    var context = ResilienceContextPool.Shared.Get(
        continueOnCapturedContext: true, cancellationToken: scheduledToken);
    try
    {
        await pipeline.ExecuteAsync(ctx => SaveAsync(ctx.CancellationToken), context);
    }
    finally
    {
        ResilienceContextPool.Shared.Return(context);
    }
}, cancellationToken);
```

Dedicated/current-thread flows return captured continuations to their thread. An outer `ConfigureAwait(true)` does not control Polly's internal awaits. Don't share a context between concurrent calls; explicit contexts also bypass the pipeline's context-pool defaults.

When work must stay on that thread:

- Use built-in delays/jitter or synchronous delay generators. In Polly 8.8.0, an async `DelayGenerator` can move later attempts off-thread despite context capture.
- `ShouldHandle` and `OnRetry` respect Polly's capture setting, but callback code can still leave the thread. Breaker transition callbacks run in the background.
- Avoid hedging or strategies that start concurrent or detached work when operations must not overlap. One thread does not prevent async operations from overlapping.

## Dependency injection

Add `Polly.Extensions`, `Microsoft.Extensions.DependencyInjection`, and `TaskFlow.Microsoft.Extensions.DependencyInjection`. Reuse the retry options above:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Polly.Registry;

var services = new ServiceCollection();
services.AddResiliencePipeline("storage", builder => builder.AddRetry(retryOptions));
services.AddTaskFlow("storage");

await using var root = services.BuildServiceProvider();
var sharedPipeline = root.GetRequiredService<ResiliencePipelineProvider<string>>()
    .GetPipeline("storage");
await using var storage = root.GetRequiredService<ITaskFlowFactory>()
    .CreateTaskFlow("storage");
await storage.Enqueue(
    token => sharedPipeline.ExecuteAsync(SaveAsync, token).AsTask(),
    cancellationToken);
```

The application owns the pipelines. Dispose flows before the provider. A shared pipeline shares breaker/limiter state across calls and keys; retry counts are per call. Use separate registrations to isolate resources. Don't rebuild pipelines per attempt or register one for every possible scheduling key.

For keyed scheduling or explicit admission with DI, use the [scoped native registration](../dependency-injection.md#keyed-scheduling-and-admission).

## Further reading

- [Polly retry](https://www.pollydocs.org/strategies/retry.html) and [timeout](https://www.pollydocs.org/strategies/timeout.html)
- [Resilience contexts](https://www.pollydocs.org/advanced/resilience-context.html) and [dependency injection](https://www.pollydocs.org/advanced/dependency-injection.html)
