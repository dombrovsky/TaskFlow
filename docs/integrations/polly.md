---
layout: page
title: Polly v8 resilience pipelines with TaskFlow
description: Use Polly retry, timeout, and dependency injection inside TaskFlow operations while preserving FIFO and keyed scheduling.
permalink: /integrations/polly/
---

# Polly v8 resilience pipelines with TaskFlow

Use Polly directly **inside the delegate submitted to TaskFlow**. No `TaskFlow.Polly` package is required. TaskFlow owns scheduling, admission, and operation lifetime; the application owns Polly pipeline configuration and sharing.

Install `TaskFlow` and `Polly.Core` in your application. Add `Polly.Extensions` and `Microsoft.Extensions.DependencyInjection` for Polly's dependency-injection registration and the service container, and `TaskFlow.Microsoft.Extensions.DependencyInjection` for TaskFlow's named factories. The examples below target .NET 8 or .NET 10 and Polly **8.8.0**, using current TaskFlow APIs. Polly is not a dependency of TaskFlow itself.

All examples use the following imports and accept application-provided I/O delegates. Copy `RetryExample` too when another example calls its retry factory.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Registry;
using Polly.Retry;
using Polly.Timeout;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Flow;
```

## Retry one logical operation

<!-- snippet: retry -->
```csharp
internal static class RetryExample
{
    public static ResiliencePipeline Create(
        int maxRetries = 3, bool useJitter = true, TimeProvider? timeProvider = null) =>
        new ResiliencePipelineBuilder { TimeProvider = timeProvider ?? TimeProvider.System }
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(
                    error => error.StatusCode is HttpStatusCode.RequestTimeout
                        or HttpStatusCode.TooManyRequests
                        or HttpStatusCode.ServiceUnavailable),
                MaxRetryAttempts = maxRetries,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = useJitter
            })
            .Build();

    public static async Task RunAsync(
        Func<CancellationToken, ValueTask> saveAsync,
        CancellationToken cancellationToken)
    {
        var pipeline = Create(maxRetries: 3, useJitter: true);
        await using var flow = new TaskFlow();
        await flow.Enqueue(
            scheduledToken => pipeline.ExecuteAsync(saveAsync, scheduledToken).AsTask(),
            cancellationToken);
    }
}
```
<!-- /snippet -->

The nongeneric `ExecuteAsync` returns `ValueTask`; `.AsTask()` selects TaskFlow's asynchronous `Task` overload. Returning a bare nongeneric `ValueTask` from an ordinary `Enqueue` lambda can select the synchronous result overload and complete the queue turn early. Result-bearing `ValueTask<T>` uses the asynchronous generic overload, as in the keyed example.

`MaxRetryAttempts` counts retries **in addition to the initial attempt**: three permits at most four calls. Set `maxRetries` from application configuration; use `useJitter: false` when deterministic delays are appropriate. The predicate retries only explicitly classified transient failures. Adapt it to your resource; do not retry every exception or treat caller cancellation as transient.

Forward the scheduled delegate's token to Polly, then forward Polly's attempt token to the actual I/O. Do not close over the original caller token: the scheduled token also carries TaskFlow's cancellation transformations and, on a token-correct execution path, shutdown. Polly can skip the attempt callback when that token is already canceled, even on a built-in flow that invokes accepted delegates with canceled tokens. Invoke the operation afresh on each attempt; do not cache a Task or ValueTask and repeatedly await it.

The pipeline starts when the scheduled delegate executes. All sequential attempts and backoff belong to **one logical queue position**. Later work remains behind it until the pipeline settles, even while a delay uses no physical worker thread. Choose an idempotent operation or protect repeated side effects; retrying a delegate repeats its whole body. For a long-running loop, it may be better to retry one iteration or resource call.

## Retrying execution versus resubmitting work

<!-- snippet: resubmit -->
```csharp
internal static class ResubmissionExample
{
    // Deliberately resubmits: this does not retain one queue position.
    public static async Task RunAsync(
        Func<CancellationToken, ValueTask> saveAsync,
        CancellationToken cancellationToken)
    {
        var pipeline = RetryExample.Create();
        await using var flow = new TaskFlow();
        await pipeline.ExecuteAsync(
            token => new ValueTask(flow.Enqueue(ct => saveAsync(ct).AsTask(), token)),
            cancellationToken);
    }
}
```
<!-- /snippet -->

This second pattern wraps `Enqueue` itself. A handled operation failure causes another submission, so later queued work can run before the next attempt. Each submission passes through admission again and can acquire another reservation. Wrapping the returned completion task also observes execution failures, so it is not automatically a policy that retries admission failures only.

Use the first pattern to retry execution in place. If you intentionally retry scheduling or admission rejection, select those failures explicitly and account for duplicate submission, ordering, and side effects. In particular, TaskFlow can return a caller-facing timeout while underlying work still runs; resubmission can then repeat work that has not stopped.

## Keyed scheduling and admission

<!-- snippet: keyed -->
```csharp
internal static class KeyedExample
{
    public static async Task RunAsync(
        Func<string, CancellationToken, ValueTask<int>> readVersionAsync,
        CancellationToken cancellationToken)
    {
        var pipeline = RetryExample.Create();
        await using var owner = new TaskFlow();
        var admission = new BoundedAdmissionController(new BoundedAdmissionOptions
        {
            Capacity = 16,
            CapacityMode = TaskSchedulerCapacityMode.Outstanding
        });
        var scheduler = owner.WithKeyedScheduling<string>()
            .WithConcurrencyLimit(2)
            .WithBoundedAdmission(admission);

        Func<string, CancellationToken, ValueTask<int>> operation =
            (key, token) => pipeline.ExecuteAsync(
                attemptToken => readVersionAsync(key, attemptToken), token);

        var first = admission.Submit(scheduler, "document-17", operation,
            TaskSchedulerAdmissionMode.Wait, cancellationToken);
        await first.Admission;
        Task<int> sameKey = scheduler.Enqueue("document-17", operation, cancellationToken);
        Task<int> otherKey = scheduler.Enqueue("document-18", operation, cancellationToken);
        await Task.WhenAll(first.Completion, sameKey, otherKey);
    }
}
```
<!-- /snippet -->

The native `WithKeyedScheduling<string>()` branch preserves FIFO for each key. Retrying inside its delegate retains that key's execution turn: later operations for the same key remain queued. Other keys can progress concurrently when the configured scheduler and `WithConcurrencyLimit` permit it.

Backoff holds the dispatched logical execution slot, including the concurrency-limit slot. It does not release the key between attempts. There is one TaskFlow submission and no additional admission reservation for a retry. With `Outstanding` capacity, that reservation remains until underlying work settles; `Pending` capacity ends when the delegate starts, independently of Polly attempts. See [keyed scheduling](../extensions/keyed-scheduling.md) and [bounded admission](../extensions/admission.md).

Create keyed branches on a `TaskFlow` owner, not on dedicated/current-thread flows. Do not await work queued behind the current operation for the same key, or work requiring an execution slot that the current operation holds.

## Total execution timeout, retry, and per-attempt timeout

<!-- snippet: timeout -->
```csharp
internal static class TimeoutExample
{
    public static ResiliencePipeline Create() =>
        new ResiliencePipelineBuilder()
            .AddTimeout(TimeSpan.FromSeconds(12)) // Total execution, including backoff.
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>(error =>
                        error.StatusCode == HttpStatusCode.ServiceUnavailable)
                    .Handle<TimeoutRejectedException>(),
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            })
            .AddTimeout(TimeSpan.FromSeconds(5)) // Each attempt.
            .Build();

    public static async Task RunAsync(
        Func<CancellationToken, ValueTask> readAsync,
        CancellationToken cancellationToken)
    {
        var pipeline = Create();
        await using var owner = new TaskFlow();
        var scheduler = owner.WithTimeout(TimeSpan.FromSeconds(20));
        await scheduler.Enqueue(
            scheduledToken => pipeline.ExecuteAsync(readAsync, scheduledToken).AsTask(),
            cancellationToken);
    }
}
```
<!-- /snippet -->

Polly strategies nest in registration order: **total timeout → retry → per-attempt timeout → operation**. Here the outer 12-second execution budget includes all attempts and their backoff. Each attempt gets a fresh 5-second budget. The retry predicate explicitly includes `TimeoutRejectedException` so a per-attempt timeout can trigger another attempt. The outer timeout is outside retry, so retry does not restart the total budget.

Polly throws `Polly.Timeout.TimeoutRejectedException` when its timeout expires and the callback cooperates with cancellation. Caller/shutdown cancellation ordinarily propagates as cancellation instead. Always pass the callback's token onward; forwarding only the scheduled token would bypass per-attempt timeout cancellation.

TaskFlow's `WithTimeout` has a different scope and throws `System.TimeoutException`. Its clock starts when submission reaches the timeout policy, so it can include scheduling and admission waits. Place it outside the admission stage to include those waits. A Polly timeout inside the delegate starts only when that operation executes. The example keeps both budgets explicit; see [TaskFlow timeout semantics](../extensions/reliability.md#timeout) for policy ordering.

Neither timeout forcibly terminates user code. Polly's cooperative timeout waits for the callback to settle; cancellation-ignoring code can keep running and may return success after its deadline. TaskFlow can finish the caller-facing task earlier, while the underlying operation still holds the lane/key, execution capacity, and outstanding admission reservation. Do not interpret a timed-out task as proof that shared resources are free.

## Dedicated threads and custom schedulers

Polly's `ResilienceContext.ContinueOnCapturedContext` defaults to `false`. The first attempt can enter on TaskFlow's dedicated thread while a later retry resumes elsewhere after an asynchronous backoff. TaskFlow's dedicated/current-thread flows pump a per-operation synchronization context: asynchronous continuations that capture it return to the servicing thread. A dedicated thread does not imply a special `TaskScheduler.Current`.

For a borrowed pipeline, explicitly rent a capturing context **inside the operation**, await the pipeline, and return that context in `finally`:

<!-- snippet: dedicated -->
```csharp
internal static class DedicatedThreadExample
{
    public static async Task RunAsync(
        Func<CancellationToken, ValueTask> deviceCallAsync,
        CancellationToken cancellationToken)
    {
        var pipeline = RetryExample.Create();
        await using var flow = new DedicatedThreadTaskFlow("device-io");
        await flow.Enqueue(async scheduledToken =>
        {
            var context = ResilienceContextPool.Shared.Get(
                continueOnCapturedContext: true,
                cancellationToken: scheduledToken);
            try
            {
                await pipeline.ExecuteAsync(
                    ctx => deviceCallAsync(ctx.CancellationToken), context);
            }
            finally
            {
                ResilienceContextPool.Shared.Return(context);
            }
        }, cancellationToken);
    }
}
```
<!-- /snippet -->

This is appropriate for sequential retry with built-in delays/jitter or synchronously completing delay generators, provided application callbacks also preserve the context. Simply writing `ConfigureAwait(true)` around the outer `ExecuteAsync` affects the enclosing continuation; it does **not** configure Polly's internal awaits or guarantee attempt placement.

If you control pipeline construction, you can instead set `ResiliencePipelineBuilderBase.ContextPool` to an application-defined pool whose `Get` defaults a missing `ContinueOnCapturedContext` choice to `true`. Token-taking `ExecuteAsync` overloads already rent/return contexts using the configured pool. Supplying an explicit context as above uses that context's settings and bypasses the pipeline pool's creation defaults; do not reuse a context concurrently.

### Boundaries of the guarantee

- **Polly 8.8.0 asynchronous `DelayGenerator`:** its internal await uses `ConfigureAwait(false)`. An incompletely awaited custom generator can move subsequent attempt entry off the dedicated thread even with context capture enabled. Prefer built-in delays/jitter or synchronous generators for thread-affine work. An outer capturing await cannot repair this.
- **Application callbacks:** `ShouldHandle` and `OnRetry` are awaited by Polly with its capture setting, unlike that delay-generator path. Their own code can still escape through `ConfigureAwait(false)`, `Task.Run`, or detached work. Apply the same discipline to timeout, fallback, and limiter callbacks; context capture does not constrain arbitrary application code.
- **Background callbacks:** circuit-breaker transition callbacks (`OnOpened`, `OnClosed`, `OnHalfOpened`) use Polly's background executor and may run outside TaskFlow's operation context, even when capture is enabled. Marshal resource access explicitly or keep these callbacks to thread-safe telemetry.
- **Concurrent strategies:** hedging and arbitrary concurrent/detached strategies can overlap invocations within one scheduled envelope. A single servicing thread does not prevent asynchronous overlap. Do not use whole-operation hedging for a resource requiring non-overlap; the pipeline type alone certifies neither sequential execution nor affinity.
- **Custom TaskScheduler:** TaskFlow has a known callback-placement limitation after waiting for an incomplete predecessor (C4 in the investigation). FIFO still holds, but do not rely on contended custom-scheduler callback affinity until that limitation is fixed. Polly capture cannot restore a context already lost before pipeline entry. `TaskFlowSchedulerAdapter` initially uses its supplied scheduler but adds no FIFO/non-overlap guarantee.
- **ExecutionContext:** flowing `AsyncLocal` state or assigning a `SynchronizationContext` object does not move a callback onto the required physical thread. Do not use either as a substitute for dispatching to that thread.

These boundaries follow the [verified feasibility follow-up](https://github.com/dombrovsky/TaskFlow/blob/b175d60e16057cb8e231dab4d53a24463c151e8e/docs/design/taskflow-polly-feasibility.md#independent-review-follow-up). They are version-specific; revalidate custom strategies when upgrading Polly.

## Dependency injection and pipeline ownership

<!-- snippet: di -->
```csharp
internal static class DependencyInjectionExample
{
    public static async Task RunAsync(
        Func<CancellationToken, ValueTask> saveAsync,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddResiliencePipeline("storage", builder =>
            builder.AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(
                    error => error.StatusCode == HttpStatusCode.ServiceUnavailable),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            }));
        services.AddTaskFlow("storage");

        await using var root = services.BuildServiceProvider();
        var pipeline = root.GetRequiredService<ResiliencePipelineProvider<string>>()
            .GetPipeline("storage");
        await using var flow = root.GetRequiredService<ITaskFlowFactory>()
            .CreateTaskFlow("storage");
        await flow.Enqueue(
            scheduledToken => pipeline.ExecuteAsync(saveAsync, scheduledToken).AsTask(),
            cancellationToken);
    }
}
```
<!-- /snippet -->

The application registers a shared named pipeline and resolves it through `ResiliencePipelineProvider<string>`. TaskFlow has no Polly-specific registration or ownership role. Stop/dispose flows before disposing the DI root or registry that owns their pipelines.

Sharing follows the instances the application reuses: operations and keys using one pipeline share its circuit breaker and limiter state. Separate named pipeline registrations can isolate resources; rebuilding a pipeline for every attempt resets strategy state and defeats sharing. Retry attempt counters remain per execution, while retry configuration is reused. Avoid automatically creating an unbounded pipeline registry entry for every scheduling key.

### Keyed DI limitation

The named `ITaskFlowFactory` result in the example supports ordinary `Enqueue`. It is an ownership wrapper and does **not** expose native keyed/admission capability. Configuring keyed scheduling inside a named factory chain and then calling keyed `Enqueue` or controller `Submit` on the returned wrapper fails at runtime.

For keyed DI, register the owner and native snapshot separately in a scope:

<!-- snippet: keyed-di -->
```csharp
internal static class KeyedDependencyInjectionExample
{
    public static async Task RunAsync(
        Func<CancellationToken, ValueTask> saveAsync,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddResiliencePipeline("storage", builder =>
            builder.AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(
                    error => error.StatusCode == HttpStatusCode.ServiceUnavailable),
                MaxRetryAttempts = 3
            }));
        services.AddScoped<TaskFlow>();
        services.AddScoped<ITaskScheduler>(provider =>
            provider.GetRequiredService<TaskFlow>()
                .WithKeyedScheduling<string>().WithConcurrencyLimit(2));

        await using var root = services.BuildServiceProvider();
        await using var scope = root.CreateAsyncScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<ITaskScheduler>();
        var pipeline = root.GetRequiredService<ResiliencePipelineProvider<string>>()
            .GetPipeline("storage");
        await scheduler.Enqueue("document-17",
            token => pipeline.ExecuteAsync(saveAsync, token).AsTask(), cancellationToken);
    }
}
```
<!-- /snippet -->

Resolve the snapshot within the scope and use the same inner-Polly delegate pattern as the keyed example. The scope asynchronously disposes the separately registered owner. Apply bounded admission to that native snapshot too; keep it visible for explicit controller `Submit`.

## Current cancellation compatibility

These examples use public APIs on `main` and require no execution middleware or proposed token-copy API. At validation time (2026-10-18, TaskFlow commit `4424d18`), [PR #46](https://github.com/dombrovsky/TaskFlow/pull/46) was open and unmerged. The current execution-middleware path can replace the terminal's effective token with the preterminal producer token, losing shutdown cancellation (C1 in the investigation). Direct integration cannot repair a token already lost upstream. Avoid relying on shutdown propagation through execution middleware until that fix is available; always forward the token actually supplied to the delegate.

Documentation-only direct integration is the current recommendation. Optional native scheduler-level integration could later provide centrally configured policies, but no `WithResilience` TaskFlow API or `TaskFlow.Polly` library is needed here.

## Validation and further reading

The [documentation examples and tests](https://github.com/dombrovsky/TaskFlow/tree/main/TaskFlow.Documentation.Tests) compile the C# shown here against the repository and Polly 8.8.0. They cover FIFO retry position, keyed concurrency/admission, cancellation during backoff, timeout composition, and dedicated-thread capture with synchronization signals and controlled time.

- [Polly retry configuration](https://www.pollydocs.org/strategies/retry.html)
- [Polly timeout strategy](https://www.pollydocs.org/strategies/timeout.html)
- [Polly resilience contexts](https://www.pollydocs.org/advanced/resilience-context.html)
- [Polly dependency injection](https://www.pollydocs.org/advanced/dependency-injection.html)
- [Polly pipeline registry and disposal](https://www.pollydocs.org/pipelines/resilience-pipeline-registry.html)
