---
layout: page
title: Polly v8 resilience pipelines with TaskFlow
description: Use Polly retry, timeout, and dependency injection inside TaskFlow operations while preserving FIFO and keyed scheduling.
permalink: /integrations/polly/
---

# Polly v8 resilience pipelines with TaskFlow

Run Polly **inside the delegate submitted to TaskFlow** to retry an operation without giving up its queue position.

Install `TaskFlow` and `Polly.Core`. These examples use Polly 8.8.0 on .NET 8/10. `SaveAsync(CancellationToken)` is an application method returning `ValueTask`; `cancellationToken` is the caller's token.

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

`MaxRetryAttempts` counts retries **after the initial attempt**: three allows at most four calls. Filter failures explicitly for your resource, exclude cancellation, and retry only repeatable operations or protect duplicate side effects.

Pass TaskFlow's scheduled token into Polly and Polly's attempt token into I/O. The method-group call above forwards both. Do not capture the original caller token inside the operation or repeatedly await a previously created Task/ValueTask.

The nongeneric `ExecuteAsync` returns `ValueTask`. Use `.AsTask()` here: an ordinary `Enqueue` lambda returning a bare nongeneric `ValueTask` can select the synchronous-result overload and finish the queue turn early. Result-bearing `ValueTask<T>` uses the asynchronous generic overload.

### Retrying execution versus resubmitting

All sequential attempts and backoff above occupy one FIFO turn. Later operations remain queued until the pipeline settles, even while no worker thread is needed for backoff.

Wrapping `Enqueue` instead retries the submission:

```csharp
await pipeline.ExecuteAsync(
    token => new ValueTask(flow.Enqueue(ct => SaveAsync(ct).AsTask(), token)),
    cancellationToken);
```

Each retry takes a new queue position and passes through admission again; later work can overtake it. This call also observes execution failures, so it is not automatically an admission-only retry policy. Resubmitting after a caller-facing TaskFlow timeout can duplicate work that is still running.

## Keyed scheduling

Reuse the retry pipeline with the native keyed API. Here `ReadVersionAsync(string, CancellationToken)` returns `ValueTask<int>`:

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

Retries retain the key's turn; later same-key operations stay queued. Different keys may progress concurrently when execution capacity permits. Backoff holds the logical execution slot, including the concurrency-limit slot.

Adding [bounded admission](../extensions/admission.md) to this native snapshot still creates only one reservation per submission, not one per retry. `Outstanding` capacity lasts until underlying work settles; `Pending` capacity ends when the delegate starts. See [keyed scheduling](../extensions/keyed-scheduling.md) for supported owners, capacity ordering, and same-key deadlocks.

## Total and per-attempt timeouts

Strategies nest in registration order: **total timeout → retry → per-attempt timeout → operation**.

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

Polly throws `TimeoutRejectedException` when its timeout expires and the callback cooperates. Including that exception in the retry predicate allows per-attempt timeouts to retry; the outer timeout is outside retry and does not restart. Caller/shutdown cancellation normally propagates as cancellation.

TaskFlow's `WithTimeout` throws `System.TimeoutException` and can include queue/admission waiting; a Polly timeout inside the delegate starts only when execution begins. Place TaskFlow's timeout outside admission to include that wait. See [TaskFlow timeout semantics](../extensions/reliability.md#timeout).

Neither timeout forcibly terminates code. Polly waits for its callback to settle; cancellation-ignoring code may even return success after the deadline. TaskFlow can complete the caller's task earlier while underlying work still holds its lane/key, execution slot, and outstanding reservation.

## Dedicated threads and context capture

Polly defaults `ContinueOnCapturedContext` to `false`, so a retry after asynchronous backoff can leave TaskFlow's dedicated thread. Rent a capturing context inside the operation when thread affinity matters:

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

Dedicated/current-thread flows return captured continuations to their servicing thread. An outer `ConfigureAwait(true)` alone does not configure Polly's internal awaits or guarantee attempt placement. Explicit contexts must not be shared concurrently; they also bypass a pipeline's configured context-pool creation defaults.

Use sequential strategies with built-in delays/jitter or synchronously completing delay generators for thread-affine work:

- In Polly 8.8.0, an asynchronously completing custom `DelayGenerator` can move later attempts off-thread even with capture enabled.
- `ShouldHandle` and `OnRetry` are awaited with Polly's capture setting, but application callbacks can still escape through their own awaits or detached work. Breaker transition callbacks use Polly's background executor.
- Hedging and arbitrary concurrent/detached strategies can overlap callbacks within one scheduled operation. They are unsuitable for whole operations requiring non-overlap; one physical thread does not prevent asynchronous overlap.

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

Pipeline ownership stays with the application/DI registry. Dispose flows before the provider. Reusing a pipeline shares its breaker/limiter state across operations and scheduling keys; separate registrations can isolate resources. Retry counters remain per execution. Do not rebuild pipelines per attempt or create unbounded registry entries for scheduling keys.

Named factory wrappers do not expose native keyed/admission capabilities. For those APIs, use the [scoped native registration](../dependency-injection.md#keyed-scheduling-and-admission) instead.

## Further reading

- [Polly retry](https://www.pollydocs.org/strategies/retry.html) and [timeout](https://www.pollydocs.org/strategies/timeout.html)
- [Resilience contexts](https://www.pollydocs.org/advanced/resilience-context.html) and [dependency injection](https://www.pollydocs.org/advanced/dependency-injection.html)
