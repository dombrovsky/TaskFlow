---
layout: page
title: TaskFlow.Polly feasibility and architecture
permalink: /design/taskflow-polly-feasibility/
---

# TaskFlow.Polly feasibility and architecture

Research date: 2026-10-16. This is a design investigation, not an integration implementation or an API commitment.

## 1. Executive summary

**An optional TaskFlow.Polly package is feasible using current public APIs, provided the initial integration runs Polly inside the scheduled operation and excludes concurrent orchestration such as hedging. No TaskFlow core change is required for that MVP.** A universal promise that any supplied `ResiliencePipeline` preserves every TaskFlow guarantee would be incorrect.

Recommend a small **per-operation delegate integration**, preceded by documentation showing direct Polly execution. It should accept existing `ResiliencePipeline` and `ResiliencePipeline<TResult>` instances, forward the actual delegate cancellation token, and create a fresh Polly execution context with context capture enabled. Keep TaskFlow scheduling, admission, ownership, and disposal in TaskFlow. Keep retry predicates, backoff, jitter, breaker state, fallback, and telemetry in Polly.

A scheduler-wide `scheduler.WithResilience(pipeline)` is attractive but is not the best first package surface. Native execution middleware supports multiple attempts within one queue turn, but has two concrete cancellation limitations: it loses the terminal's effective token, and its public operation context cannot carry Polly's replacement timeout token downstream. An opaque scheduler wrapper can avoid these token problems for ordinary `Enqueue`, but hides native keyed and explicit-admission capabilities. Neither workaround justifies adding core APIs merely for convenience.

Before implementing a fully composable **native middleware decorator**, resolve these general-purpose execution-token limitations. Before promising dedicated-thread affinity for arbitrary pipelines, also address Polly-specific context escapes: Polly's default context does not capture affinity; in 8.8.0 an asynchronously completing retry `DelayGenerator` uses `ConfigureAwait(false)` even when capture is enabled. Circuit-breaker transition callbacks have their own background executor. Hedging can overlap user invocations even on a dedicated thread.

There is **no native production retry policy to replace** in the inspected TaskFlow revisions. Adjust issue #9 toward an optional integration and explicit execution semantics, retaining TaskFlow's dependency-free core and its existing timeout/admission facilities.

## 2. Evidence and revision boundaries

The supplied working checkout is [`34dc96ee49db4f817a8ac0c8209a46f46a164506`](https://github.com/dombrovsky/TaskFlow/commit/34dc96ee49db4f817a8ac0c8209a46f46a164506), after bounded-admission PR #34. During investigation, remote `main` was [`4424d181273cba2fdf37ef51c39781accb6d40bd`](https://github.com/dombrovsky/TaskFlow/commit/4424d181273cba2fdf37ef51c39781accb6d40bd), which includes merged keyed-scheduling PR #36. Both implementations were inspected. The working branch was not advanced or rebased. Relative TaskFlow source links below refer to the supplied checkout; keyed references link explicitly to the newer upstream revision. The execution-token findings remain present in both revisions.

[NuGet's Polly.Core version index](https://api.nuget.org/v3-flatcontainer/polly.core/index.json) listed **8.8.0** as the latest stable version. Polly source and its bundled official documentation were inspected at [tag 8.8.0, commit `9ad5ae9b4ff9beb4321b0762249ce173b984f817`](https://github.com/App-vNext/Polly/tree/8.8.0). These are version-specific conclusions; recheck the supported version before implementation.

Evidence labels used here:

- **Inspected:** follows from implementation, public contracts, or official documentation; not a claim of an integration test passing.
- **Observed:** executed during this investigation on .NET 10 with Polly.Core 8.8.0.
- **To prototype:** a proposed adapter behavior or broader configuration not tested here.

Reviewed [issue #9](https://github.com/dombrovsky/TaskFlow/issues/9) (open, no comments when fetched), [middleware issue #24](https://github.com/dombrovsky/TaskFlow/issues/24) and [PR #25](https://github.com/dombrovsky/TaskFlow/pull/25), [exception-context PR #26](https://github.com/dombrovsky/TaskFlow/pull/26), [admission issue #30](https://github.com/dombrovsky/TaskFlow/issues/30) and [PR #34](https://github.com/dombrovsky/TaskFlow/pull/34), [keyed issue #11](https://github.com/dombrovsky/TaskFlow/issues/11) and [PR #36](https://github.com/dombrovsky/TaskFlow/pull/36), [timeout issue #31](https://github.com/dombrovsky/TaskFlow/issues/31) and [PR #32](https://github.com/dombrovsky/TaskFlow/pull/32), and [synchronous admission issue #37](https://github.com/dombrovsky/TaskFlow/issues/37). Proposals in those discussions were checked against code rather than treated as existing APIs.

## 3. Existing TaskFlow architecture

### Terminal contract and composition

[`ITaskScheduler.Enqueue<T>`](../../TaskFlow/ITaskScheduler.cs) has one canonical signature: a state-bearing `Func<object?, CancellationToken, ValueTask<T>>`, returning `Task<T>`. [`TaskSchedulerEnqueueExtensions`](../../TaskFlow/TaskSchedulerEnqueueExtensions.cs) adapts synchronous, `Task`, `ValueTask`, generic, nongeneric, and typed-state delegates. Nongeneric operations use internal `Unit`; an external adapter should not expose or depend on that internal type. Direct value-returning async lambdas can be ambiguous between Task and ValueTask overloads.

[`ITaskFlow`](../../TaskFlow/ITaskFlow.cs) adds options and disposal. `ITaskScheduler` alone promises neither a FIFO lane nor ownership: [`TaskFlowSchedulerAdapter`](../../TaskFlow/TaskFlowSchedulerAdapter.cs) and custom implementations can have different scheduling and cancellation behavior.

[`TaskSchedulerMiddlewareExtensions.UseMiddleware`](../../TaskFlow/Middleware/TaskSchedulerMiddlewareExtensions.cs) creates immutable, non-owning snapshots. [`PipelineTaskScheduler`](../../TaskFlow/Internal/TaskSchedulerPipeline.cs) keeps the terminal, registrations, annotation scopes, and optional shared lifetime. It treats unknown wrappers as opaque boundaries.

| Phase | Order | Relevant contract |
| --- | --- | --- |
| Enqueue | Newest registration first | `ITaskSchedulerEnqueueMiddleware`; continuation may run zero or one time. Waiting/rejection happens before terminal scheduling. |
| Execution | Registration order | `ITaskSchedulerExecutionMiddleware`; continuation may run repeatedly inside the same terminal envelope. This is the natural scheduler-decorator retry seam. |
| Completion | Registration order | `ITaskSchedulerCompletionMiddleware`; one claimed logical completion, with result/exception transformation. It is outside the retry attempt loop. |

Annotations are forward-scoped. Each registration captures its existing annotation scope; later names do not retroactively rename earlier policies. Registration-local state is **per logical operation**, not automatically per retry attempt. See [`TaskSchedulerOperationContext`](../../TaskFlow/Middleware/TaskSchedulerOperationContext.cs), [`PipelineOperation`](../../TaskFlow/Internal/PipelineOperation.cs), and [customization documentation](../customization.md).

### FIFO and execution

[`TaskFlow.Enqueue` and `RunAfterPrevious`](../../TaskFlow/TaskFlow.cs) assign predecessors under `ThisLock`. Each terminal task awaits the previous task; the stored continuation allows progression after faults. FIFO means the accepted terminal order, not a deterministic wall-clock order for racing concurrent producers. Asynchronous enqueue policies can affect when work reaches the terminal.

[`ThreadTaskFlow.ThreadStart` and `ExecutionItem.Execute`](../../TaskFlow/ThreadTaskFlow.cs) consume queued items sequentially and pump a per-operation synchronization context until the asynchronous operation finishes. [`DedicatedThreadTaskFlow`](../../TaskFlow/DedicatedThreadTaskFlow.cs) owns the thread; [`CurrentThreadTaskFlow`](../../TaskFlow/CurrentThreadTaskFlow.cs) uses the application's `Run` thread. Captured asynchronous continuations return to that thread. User code can intentionally escape it.

Sequential built-in flows invoke accepted queued delegates even with already-canceled tokens. They link caller cancellation to `TaskFlowBase.CompletionToken`. The adapter instead supplies its caller token to `Task.Factory.StartNew`, so it can cancel before invocation. An integration must preserve this distinction at the scheduling boundary while documenting that Polly itself can skip the inner user callback when already canceled.

### Admission and lifetime

[`BoundedAdmissionController.Submit`](../../TaskFlow/Admission/BoundedAdmissionController.cs) returns [`TaskSchedulerSubmission`](../../TaskFlow/Admission/TaskSchedulerSubmission.cs): separate `Admission` and `Completion` tasks. `Enqueue` uses configured defaults; `Submit(Wait)` applies asynchronous backpressure; `Submit(Reject)` avoids capacity waiting when the admission stage is reached. **Reject is not a guaranteed synchronous decision.** Upstream middleware can defer reaching that stage. No atomic synchronous `TrySubmit` API exists in either inspected revision; #37 proposes it. Do not block on `Admission.Result` under producer locks.

[`BoundedAdmissionMiddleware`](../../TaskFlow/Internal/BoundedAdmissionMiddleware.cs) uses a FIFO dispatch pump, not just semaphore wake order. Outstanding capacity lasts until operation quiescence. Pending capacity lasts until `PipelineOperation.MarkStarted` (actual user invocation), or quiescence if the operation never starts. `Submission<T>.ReleaseOnce` prevents duplicate release. A caller-facing timeout does not free still-running work's reservation.

[`TaskSchedulerMiddlewareLifetime`](../../TaskFlow/Middleware/TaskSchedulerMiddlewareLifetime.cs) tracks pipeline entry through underlying work, including preterminal waits and early caller completion. Factory-created registrations belong to that lifetime; caller-supplied middleware remains caller-owned. Snapshots share already-created registrations. Custom terminals require an explicit lifetime for lifetime-dependent factories.

[`TaskFlowBase.DisposeAsyncCore` and `ShutdownAsync`](../../TaskFlow/TaskFlowBase.cs) close registration/submission entry, signal stopping, request terminal cancellation, wait for terminal work and pipeline submissions, then clean up owned registrations in reverse order. Operation failures remain on operation tasks; cleanup failures are aggregated separately. Synchronous disposal may time out while cleanup continues. There is no separate public graceful-drain-without-cancellation mode. Reentrant disposal from the same owned submission is rejected.

## 4. Polly APIs and recommended package design

### Dependency boundary

The actual core NuGet package is named **TaskFlow**, not TaskFlow.Core; this report uses “core” to mean that assembly/package. Proposed dependency direction:

```text
TaskFlow.Polly -> TaskFlow
              -> Polly.Core

Application -> Polly.Extensions       (optional DI/telemetry configuration)
            -> Polly.RateLimiting     (optional limiter strategy configuration)
```

Do not add Polly to TaskFlow, or reference Polly's legacy `Polly` package. Follow repository package metadata, target frameworks, signing, README, and public-API manifest conventions. TaskFlow targets netstandard2.0/net8.0/net10.0; Polly.Core 8.8.0 includes compatible netstandard2.0 and net8.0 assets. Runtime tests must still cover the chosen assets; compatibility is not established merely by matching framework names. See [Polly package roles](https://github.com/App-vNext/Polly/blob/8.8.0/README.md) and [Polly.Core project targets](https://github.com/App-vNext/Polly/blob/8.8.0/src/Polly.Core/Polly.Core.csproj).

`ResiliencePipeline` can execute both void and result-bearing delegates. It is not limited to nongeneric operations. `ResiliencePipeline<TResult>` supplies typed predicates and fallback results; its async overloads constrain compatible result types. Use the result type at compile time rather than an untyped scheduler decorator that silently skips mismatched results or fails only when an unrelated operation is submitted. See [`ResiliencePipeline.AsyncT`](https://github.com/App-vNext/Polly/blob/8.8.0/src/Polly.Core/ResiliencePipeline.AsyncT.cs) and [`ResiliencePipelineT.Async`](https://github.com/App-vNext/Polly/blob/8.8.0/src/Polly.Core/ResiliencePipelineT.Async.cs).

### Initial surface: attach resilience to the operation

Proposed signatures, **not existing APIs**:

```csharp
Func<CancellationToken, ValueTask> WithResilience(
    this Func<CancellationToken, ValueTask> operation,
    ResiliencePipeline pipeline);

Func<CancellationToken, ValueTask<TResult>> WithResilience<TResult>(
    this Func<CancellationToken, ValueTask<TResult>> operation,
    ResiliencePipeline pipeline);

Func<CancellationToken, ValueTask<TResult>> WithResilience<TResult>(
    this Func<CancellationToken, ValueTask<TResult>> operation,
    ResiliencePipeline<TResult> pipeline);
```

Use the repository's `System.Threading.Tasks.Flow` extension namespace convention. Supply a small set of Task-returning overloads if justified by caller examples; avoid reproducing the entire enqueue-overload matrix initially. No new scheduler interface or execution owner is necessary.

Illustrative usage of the proposed helper:

```csharp
Func<CancellationToken, ValueTask<Order>> save = SaveAsync;
var resilientSave = save.WithResilience(orderPipeline);

await scheduler.Enqueue(resilientSave, callerToken);

// Same native admission path and separate completion task:
var submission = admission.Submit(
    scheduler, resilientSave, TaskSchedulerAdmissionMode.Wait, callerToken);
await submission.Admission;
await submission.Completion;

// On the newer keyed revision, keep the native scheduler visible:
await keyedScheduler.Enqueue(documentId, resilientSave, callerToken);
```

The returned delegate must start Polly **when invoked by TaskFlow**, not while constructing the delegate or submitting it. It must obtain a new Polly context for each execution, pass the scheduled delegate's token into that context, forward `ResilienceContext.CancellationToken` to every attempt, await full pipeline completion, and return the context in `finally`. Never cache a `ValueTask`, reuse a context concurrently, call `Task.Run` as an adapter mechanism, or retry a previously obtained task instead of invoking the operation again. State-bearing delegates can use the same pattern without replacing caller-visible state; typed-state helpers can be deferred until needed.

This reduces meaningful boilerplate: correct context pooling, context capture, and effective attempt-token forwarding. A helper that only writes `pipeline.ExecuteAsync(operation, token)` adds little and misses the affinity issue. An `EnqueueResilient` convenience extension could be added later, but delegate composition already works with native Enqueue, Submit, and keyed APIs without duplicating them.

### Documentation-only usage works today

This is ordinary application code using existing public APIs, shown for a result-bearing operation:

```csharp
Task<Order> scheduled = scheduler.Enqueue<Order>(
    async (_, scheduledToken) =>
    {
        var context = ResilienceContextPool.Shared.Get(
            continueOnCapturedContext: true,
            cancellationToken: scheduledToken);
        try
        {
            return await orderPipeline.ExecuteAsync<Order>(
                ctx => SaveAsync(ctx.CancellationToken), context)
                .ConfigureAwait(true);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    },
    state: null,
    cancellationToken: callerToken);
```

Use the delegate token rather than closing over `callerToken`. The former includes shutdown and enqueue-policy transformations when it reaches the actual delegate. The context-capture setting has the restrictions in section 7; it does not make every possible pipeline thread-affine.

### DI, sharing, and ownership

Applications can obtain pipelines from `Polly.Registry.ResiliencePipelineProvider<TKey>.GetPipeline(key)` or `GetPipeline<TResult>(key)` and pass them to the helper. The provider/registry abstractions are in Polly.Core; `AddResiliencePipeline` registration and standard telemetry integration are in Polly.Extensions. No Microsoft DI dependency is necessary in TaskFlow.Polly. TaskFlow already accepts an `IServiceProvider` in its named-chain configuration via [`DelegateNamedConfigureTaskFlowChain`](../../TaskFlow.Extensions.Microsoft.DependencyInjection/Internal/DelegateNamedConfigureTaskFlowChain.cs).

Treat supplied pipelines and providers as **borrowed**. Do not create a pipeline per attempt: that would reset breaker state and limiter sharing. Choose sharing by protected resource: one pipeline across keys shares its breaker and limiter; separate provider keys can isolate them. Do not create unbounded per-TaskFlow-key pipelines automatically.

The adapter must not dispose a provider, registry, caller-owned limiter, or terminal flow. In 8.8.0 the pipeline classes do not expose a public IDisposable contract; registry-managed components and `OnPipelineDisposed` callbacks handle owned resources. Arrange application shutdown so flows stop using pipelines before the registry/DI root is disposed. Preserve provider-managed reload behavior; do not clone pipelines or rebuild strategies in the adapter. See [official DI documentation](https://www.pollydocs.org/advanced/dependency-injection.html), [registry resource disposal](https://www.pollydocs.org/pipelines/resilience-pipeline-registry.html#resource-disposal), and [`ResiliencePipelineRegistry`](https://github.com/App-vNext/Polly/blob/8.8.0/src/Polly.Core/Registry/ResiliencePipelineRegistry.cs). Cross-system reload/shutdown races remain an integration-test requirement.

## 5. Strategy compatibility matrix

“Inside operation” below means within a single scheduled delegate. It does not mean wrapping `scheduler.Enqueue` in Polly and repeatedly submitting new work.

| Strategy | Suitability and preferred integration | Scheduling implications, overlap, and restrictions |
| --- | --- | --- |
| Retry | **Suitable inside the operation.** Polly supplies exponential backoff, jitter, delay generation, exception/result predicates, and OnRetry. Usually wrap the complete logical operation; wrap a smaller external call if repeating earlier side effects is unsafe. | Attempts and delays occupy one lane/key turn. No extra TaskFlow admission reservation. Require repeatable/idempotent operations or explicit duplicate protection. Exclude cancellation from retry predicates. Ordinary backoff can retain affinity with context capture; asynchronously completing DelayGenerator is an 8.8.0 exception. TaskFlow has no native production retry feature to duplicate. |
| Timeout | **Suitable with attempt-token forwarding.** Place inside the scheduled operation for execution-only or per-attempt limits. Keep TaskFlow timeout for a caller budget including queue/admission wait. | Cooperative; does not release the lane while the callback is still running. Polly replaces the callback token and can throw TimeoutRejectedException after cooperative cancellation. Current native execution middleware cannot forward that replacement token cleanly. Not equivalent to TaskFlow WithTimeout. |
| Circuit breaker | **Suitable inside the operation.** Evaluate the resource's circuit when its queue turn executes. A deliberate pre-admission breaker is a different policy. | An open circuit skips the user callback but does not reorder surviving queue turns. It does not cap TaskFlow backlog: already queued work must still reach its turn. Share state intentionally across keys/resources; serialized low-throughput flows may never meet MinimumThroughput. Transition callbacks use Polly's background executor, so no TaskFlow thread-affinity promise for those callbacks. |
| Rate limiter | **Conditionally suitable inside the operation**, often around the outbound resource call. Building it requires Polly.RateLimiting in the application. | Waiting for a lease holds the TaskFlow lane/key/execution slot. A retry outside the limiter acquires a lease per attempt; limiter outside retry holds one lease over delays/attempts. A pre-admission limiter could avoid lane occupancy but introduces a second queue and different ordering/lifetime semantics. Use native TaskFlow admission to bound retained submissions. WithThrottle already provides a simpler leading-edge rejection policy, but is not a token/sliding-window limiter. |
| Concurrency limiter | **Conditionally useful as a shared resource bulkhead across flows/keys.** Prefer native TaskFlow execution limits for TaskFlow branch concurrency. | Redundant on an isolated sequential lane. An inner shared limiter can reduce concurrency across multiple lanes; it cannot make a single lane concurrent. Dual queues and holding a permit during backoff can cause head-of-line blocking. Never await a resource permit whose release requires later work on the same blocked lane. It does not atomically reserve TaskFlow global/per-key admission budgets. |
| Hedging | **Exclude from whole-operation MVP support.** Not safe as a general scheduler decorator or around arbitrary same-key mutations. | Can invoke downstream concurrently within a single logical operation. Dedicated-thread execution still permits async overlap. Attempts bypass per-operation admission and execution-slot counting, and can share registration-local state unsafely. Polly 8.8.0 cancels and awaits losing attempts during cleanup; do not inaccurately describe its built-in hedges as detached by default. Cancellation-ignoring losers can delay completion indefinitely. Prefer application-controlled hedging of independent, thread-safe external requests followed by one serialized state update. |
| Fallback | **Suitable, generally with ResiliencePipeline<TResult>.** Prefer outside retry for one final recovery, or around the particular resource call. | Sequential fallback remains in the same queue turn and changes a failed logical outcome into success. OnError will then see no failure; use OnFallback telemetry. Never hide shutdown cancellation accidentally. Fallback side effects must obey the same lane rules and be awaited. A typed fallback cannot sensibly apply to all unrelated result types of ITaskScheduler. |
| Combinations | **Suitable only after reviewing order and every constituent strategy.** Common execution pipeline: total timeout → retry → per-attempt timeout → operation; fallback can surround that pipeline. | Builder order is outermost to innermost. Retry → breaker counts attempts; breaker → retry observes the final execution outcome. Broad retry predicates may retry BrokenCircuitException or RateLimiterRejectedException needlessly. Any hedging/custom concurrent strategy invalidates blanket non-overlap guarantees regardless of other strategies. |

Sources: official [retry](https://www.pollydocs.org/strategies/retry.html), [timeout](https://www.pollydocs.org/strategies/timeout.html), [circuit breaker](https://www.pollydocs.org/strategies/circuit-breaker.html), [rate limiter](https://www.pollydocs.org/strategies/rate-limiter.html), [hedging](https://www.pollydocs.org/strategies/hedging.html), and [fallback](https://www.pollydocs.org/strategies/fallback.html) documentation; implementation details were checked in the corresponding strategies under [Polly.Core](https://github.com/App-vNext/Polly/tree/8.8.0/src/Polly.Core) and [`RateLimiterResilienceStrategy.ExecuteCore`](https://github.com/App-vNext/Polly/blob/8.8.0/src/Polly.RateLimiting/RateLimiterResilienceStrategy.cs).

Polly hedging also has a documented sequential mode using a delay of -1 millisecond. That avoids concurrent attempts for that configuration, but custom DelayGenerator/ActionGenerator behavior and context still need review. It does not justify supporting arbitrary hedging pipelines in the MVP; retry already serves sequential retry use cases more naturally.

## 6. FIFO, keyed execution, admission, cancellation, and disposal

### Retries occupy the original queue position

For a pipeline awaited inside scheduled operation A, the sequence is:

```text
one terminal turn: [A attempt 1 | backoff | A attempt 2 | final outcome]
next turn:                                                         [B]
```

Backoff asynchronously releases a physical worker thread where possible, **not the logical execution lane**. B cannot overtake A. A terminal failure completes A and allows B to proceed; retries do not poison the flow. This follows from `TaskFlow.RunAfterPrevious` and `ThreadTaskFlow.ExecutionItem.Execute`, and ordinary-backoff lane retention was observed.

By contrast, `pipeline.ExecuteAsync(token => scheduler.Enqueue(...))` puts Polly outside scheduling. A failed attempt releases its turn; B can execute during backoff; the next attempt joins the tail and may wait for capacity again. Shutdown may cause a new enqueue to fail with ObjectDisposedException rather than promptly canceling Polly's backoff, unless the external owner token is also supplied. This architecture can intentionally retry admission, but does not satisfy #9's same-position retry semantics.

Polly timeout does not intrinsically let B overtake a still-running A, because it awaits its callback. TaskFlow caller-facing timeout may finish early, but the actual terminal envelope still holds the lane. Custom strategies that start unawaited work and return early can let later operations overlap that work; no adapter can repair that contract violation through pipeline shape alone.

### Keyed execution on upstream 4424d18

[`WithKeyedScheduling<TKey>`](https://github.com/dombrovsky/TaskFlow/blob/4424d181273cba2fdf37ef51c39781accb6d40bd/TaskFlow/Keyed/KeyedTaskSchedulerExtensions.cs#L13) creates a separate flow-owned branch using a `TaskFlowSchedulerAdapter` over the owner's configured TaskScheduler. It does not submit all keys back through the owner's original serial lane. It requires a TaskFlow owner and explicitly rejects dedicated/current-thread flows and opaque/custom owners. Without an explicit execution limit, unrelated keys can dispatch concurrently; `WithConcurrencyLimit` constrains that branch.

[`KeyedCoordinator<TKey>`](https://github.com/dombrovsky/TaskFlow/blob/4424d181273cba2fdf37ef51c39781accb6d40bd/TaskFlow/Internal/KeyedCoordinator.cs#L40) maintains typed key queues and ready-key round robin. `Pump` marks a key busy and acquires execution limits only on dispatch. `Complete`, registered through `PipelineOperation.OnQuiesced`, releases key ownership, outstanding reservations, and execution limits. `Started` releases pending reservations once. Global and per-key admission are checked together without partial reservation.

With Polly inside the native keyed delegate:

- A retry keeps its original key busy across backoff. Later same-key operations remain behind it.
- Different keys may progress while it waits, provided another execution slot is available. Cross-key completion order is deliberately not global FIFO.
- Backoff holds a dispatched execution slot. With concurrency 1 it delays all keys; this is consistent with treating resilience as one logical operation, but reduces ready-key fairness opportunities.
- Outstanding capacity counts the resilient operation once until it settles. Pending capacity is released on its first actual delegate start, not reacquired on each retry. A retry is additional execution, not another queued item.
- Caller-facing timeout must not release the key or execution slot before underlying work completes. The coordinator already uses quiescence for that purpose.
- Shared pipeline breaker/limiter state can couple keys even though TaskFlow routing isolates their execution. That is an application policy choice, not key-serialization failure.

For per-operation integration, TaskFlow's scheduled delegate is the resilient wrapper: pending capacity ends when that wrapper starts, even if Polly then waits for a limiter before the business callback. With native execution middleware, MarkStarted occurs at the downstream user invocation, so a middleware limiter can retain pending capacity longer; a breaker short circuit releases it at quiescence without a start. This difference follows the existing definition of the scheduled operation and should be documented. Neither design reacquires pending capacity on retry.

These accounting conclusions are inspected, supported by existing keyed tests, and require Polly-specific gated tests before release. Hedging would run multiple attempts under one counted logical slot and break same-key operation exclusion; it is not made safe by the coordinator.

### Cancellation and shutdown

At the actual user delegate, forward the supplied scheduled token to Polly, and then forward Polly's current attempt token to the operation. This retains caller cancellation, timeout/scope transformations, and built-in shutdown linkage. Polly's retry delay observes that token; the observed hour-long backoff was interrupted by disposal without another attempt.

Polly normally excludes OperationCanceledException from reactive handling. Its nonempty pipeline entry can reject a pre-canceled execution before calling the user operation. Thus TaskFlow still invokes the accepted **envelope**, but the innermost callback may be skipped. Do not promise exactly one user callback invocation after cancellation. Empty/custom pipelines need their own policy review. Polly 8.7+ includes caller-token propagation changes in timeout and hedging; the inspected 8.8.0 timeout uses `Outcome.WithCallerCancellationToken` rather than treating caller cancellation as its own timeout.

Disposal is cooperative. It cannot interrupt blocking user code or force a callback, predicate, DelayGenerator, OnRetry, or fallback callback to finish. `RetryResilienceStrategy.ExecuteCore` checks cancellation around delays, but configuration callbacks can delay reaching that check. Cancellation must not be handled as a transient resource failure merely to keep a shutdown loop alive.

Correctly awaited retry and backoff cannot outlive their underlying logical scheduled operation. They **can** outlive its caller-facing TaskFlow timeout task, while remaining tracked. `DisposeAsync` waits for that work; finite synchronous Dispose may return first. A manually detached retry loop, custom strategy, or async-void delegate falls outside this guarantee.

The current execution-middleware token defect changes this picture: adding even pass-through execution middleware can replace the terminal-linked token with a preterminal token before the user delegate sees it. See finding C1. Therefore the no-core-change MVP claim assumes the scheduled delegate actually receives the intended token. Do not certify shutdown preservation across arbitrary current middleware chains; characterize those chains or fix C1 independently.

## 7. Execution context and concurrency

Polly async execution is not inherently a Task.Run wrapper. Its retry, timeout, limiter, and fallback generally invoke/await the callback in the current execution. However, its shared context pool defaults `ContinueOnCapturedContext` to false. Setting ConfigureAwait(true) only around the *outside* ExecuteAsync call does not control where Polly invokes its second attempt.

For dedicated/current-thread flows, obtain a fresh context **inside** the scheduled delegate with `continueOnCapturedContext: true`; await adapter work with context capture. Ordinary retry backoff then returns to the per-operation synchronization context. Observed dedicated-thread attempts changed thread with the default context and retained their thread with capture enabled.

Two important exceptions were found in Polly 8.8.0:

1. [`RetryResilienceStrategy.ExecuteCore`, line 91](https://github.com/App-vNext/Polly/blob/8.8.0/src/Polly.Core/Retry/RetryResilienceStrategy.cs#L91) awaits DelayGenerator with ConfigureAwait(false). An asynchronously completing generator moved the second attempt off the dedicated thread **even with capture enabled**, as observed. Built-in exponential/jitter calculations and synchronously completing generators do not take that asynchronous escape path. Restrict this configuration on thread-affine flows, or prototype an adapter-local callback dispatcher that explicitly returns each attempt to the captured per-operation context. Do not claim that dispatcher is already validated.
2. [`CircuitStateController`](https://github.com/App-vNext/Polly/blob/8.8.0/src/Polly.Core/CircuitBreaker/Controller/CircuitStateController.cs) schedules transition events through [`ScheduledTaskExecutor`](https://github.com/App-vNext/Polly/blob/8.8.0/src/Polly.Core/CircuitBreaker/Controller/ScheduledTaskExecutor.cs#L14), which starts a background Task.Run processing loop. OnOpened/OnClosed/OnHalfOpened must not assume TaskFlow thread or SynchronizationContext affinity. This differs from the protected operation's own context.

Hedging cannot be repaired merely by capturing context. The observed dedicated-thread experiment had **two overlapping invocations**. The thread serializes synchronous fragments while asynchronous operations overlap. [`HedgingExecutionContext.DisposeAsync`](https://github.com/App-vNext/Polly/blob/8.8.0/src/Polly.Core/Hedging/Controller/HedgingExecutionContext.cs#L76) cancels and awaits losers; [`TaskExecution`](https://github.com/App-vNext/Polly/blob/8.8.0/src/Polly.Core/Hedging/Controller/TaskExecution.cs) starts secondary actions and uses Task.Run specifically for synchronous hedging execution. Reject “all strategies preserve sequential execution” as an integration contract.

Custom TaskScheduler behavior also has a **preexisting TaskFlow limitation**. `TaskFlow.RunAfterPrevious`, line 176, awaits its predecessor with ConfigureAwait(false). When that wait is incomplete, the subsequent user invocation can leave the configured scheduler before Polly starts. A scratch custom single-thread scheduler observed TaskScheduler.Current no longer matching on the second queued operation. Polly context capture can retain the context at its own entry; it cannot retroactively fix where TaskFlow entered it. `TaskFlowSchedulerAdapter` initially invokes directly on the supplied scheduler; newer keyed branches use that adapter. Test each execution model rather than generalizing the serial TaskFlow result to all models.

`ExecutionContext`/AsyncLocal flow is distinct from thread affinity and TaskScheduler.Current. Newer keyed tests explicitly exercise submitting ExecutionContext and suppressed flow. The proposed helper must neither suppress that flow nor introduce ambient mutable retry state. No claim is made here that every arbitrary strategy or user callback preserves all ambient values.

Do not marshal attempts by calling `Enqueue` on the same currently occupied sequential lane and awaiting it: that deadlocks behind the operation holding the lane. [`TaskFlowSynchronizationContext.Post`](../../TaskFlow/TaskFlowSynchronizationContext.cs) itself enqueues new operations and is not a substitute for the thread flow's per-operation pump in such a dispatcher.

## 8. Timeout ordering and exception semantics

| Composition | Budget and observable behavior |
| --- | --- |
| TaskFlow WithTimeout around resilient delegate | Timer begins when its enqueue registration is reached. Includes later admission/queue wait, attempts, and backoff; may return early while actual work remains tracked. Throws System.TimeoutException when its timer wins. |
| Polly timeout → retry → operation, inside TaskFlow | Execution-only total resilience budget including backoff; caller/TaskFlow queue waiting precedes the clock. Cancellation during backoff ends retry; outer timeout can translate that cancellation into TimeoutRejectedException. |
| Polly retry → timeout → operation, inside TaskFlow | New timeout token/budget per attempt. Configure retry to handle TimeoutRejectedException deliberately. No overlapping attempts from timeout alone: each callback must finish before another retry begins. |
| Polly total timeout → retry → per-attempt timeout | Bounds the resilient execution and individual attempts separately; use one coherent incoming cancellation token throughout. |
| Polly around TaskFlow Enqueue | Includes whatever admission/queue wait that call performs, but retries create new queue turns. Does not implement same-position retries. Not the recommended architecture. |

TaskFlow's [`TimeoutMiddleware`](../../TaskFlow/Extensions/TimeProviderTimeoutTaskSchedulerExtensions.cs) races enqueue against a delay via [`TaskExtensions.WhenAnyCancelRest`](../../TaskFlow/Internal/TaskExtensions.cs). It observes losing faults and requests cancellation but does not await the losing operation before returning. Moving the timeout registration relative to admission changes which preterminal waiting is included; the mere order of fluent calls cannot move an enqueue timeout inside retry execution middleware.

Polly's [`TimeoutResilienceStrategy.ExecuteCore`](https://github.com/App-vNext/Polly/blob/8.8.0/src/Polly.Core/Timeout/TimeoutResilienceStrategy.cs#L43) replaces the context token, awaits the callback, restores the incoming token, and translates an OperationCanceledException only when its own timeout source fired and the incoming token was not canceled. A cancellation-ignoring callback may eventually **succeed after the timeout duration**; this was observed. A different terminal exception can also remain the outcome rather than automatically becoming a timeout. Neither implementation can forcibly stop user code.

Keep both timeout mechanisms available for their different scopes; do not silently replace TaskFlow's implementation with Polly. Do not translate Polly TimeoutRejectedException into System.TimeoutException in the adapter. They have different inheritance and meaning; `OnError<TimeoutException>` will not automatically observe Polly timeouts. An outer TaskFlow timeout and an inner Polly timeout can race, producing either policy's documented outcome. Caller cancellation must remain cancellation rather than a retryable timeout. Existing TaskFlow timeout boundary tests allow racing outcomes; do not promise deterministic winner selection at a simultaneous boundary.

## 9. Exceptions and observability

Polly's normal ExecuteAsync overloads capture exceptions into `Outcome<T>` and rethrow terminal failures with ExceptionDispatchInfo. TaskFlow's [`TaskSchedulerOperationOutcome<TResult>`](../../TaskFlow/Middleware/TaskSchedulerOperationOutcome.cs) similarly preserves dispatch information. Exhausted retry should expose the final exception, not introduce an AggregateException or a new TaskFlow retry exception. Polly timeout intentionally introduces TimeoutRejectedException with the cancellation exception inside it. Cancellation propagation can normalize token identity; TaskFlow admission completion sources also sometimes use TrySetCanceled without retaining the original token. Do not promise reference equality for all cancellation exceptions/tokens.

Predicate selection is application policy. Prefer specific transient failures/results. Do not retry shutdown cancellation, programmer failures, admission rejection, throttling cancellation, or an open breaker indiscriminately. `OperationThrottledException` derives from OperationCanceledException; TaskFlow admission rejection and Polly limiter rejection have different meanings. Retry outside scheduling sees admission failures; retry inside the scheduled operation normally does not.

With per-operation wrapping, TaskFlow interceptors observe one logical execution. Polly OnRetry, ShouldHandle, OnTimeout, OnFallback, and telemetry provide per-attempt information without core changes. Correlation can use `ResilienceContext.OperationKey` and typed properties; keep operation keys low-cardinality. State or operation-name annotations can be mapped explicitly, but an automatically inferred name is not required for the MVP.

With a future native execution decorator, registration order can select instrumentation scope: logging/interception registered before resilience surrounds the whole operation; execution interception after resilience runs for each downstream attempt. The downstream registration-local state slot remains shared across those attempts, so custom middleware must not assume a new slot per attempt. [`InterceptionTaskSchedulerExtensions`](../../TaskFlow/Extensions/InterceptionTaskSchedulerExtensions.cs) creates/copies an interceptor on invocation; evaluate its configuration accordingly. Completion middleware such as [`OnError`](../../TaskFlow/Extensions/ExceptionTaskSchedulerExtensions.cs) runs once on the selected final outcome, not for every handled failure. A callback that throws may replace the outcome, including in Polly policy callbacks; this is observable behavior, not a logging-only side effect.

An outer TaskFlow timeout can claim logical completion from the enqueue path before the resilient execution settles. Its OnError/completion callbacks can therefore run off the terminal context, and later retry activity does not create a second TaskFlow completion notification. Do not describe every completion callback as a final physical-work event or promise terminal affinity for this early-completion path. Polly callbacks remain the attempt-level evidence.

Polly.Extensions supplies standard logging/metrics and telemetry enrichment; preconfigured supplied pipelines can carry that configuration unchanged. The core integration need not depend on that package. See [Polly telemetry](https://www.pollydocs.org/advanced/telemetry.html). TaskFlow and Polly logs describe different levels of work; documentation should show one logical-operation correlation with retry attempt numbers rather than counting both as distinct jobs.

## 10. TaskFlow core limitations and prioritized changes

**No core API or internal implementation change is necessary for the per-operation MVP on correctly token-propagating paths.** The following are not blanket blockers to integration. C1/C2 are blockers to advertising a fully composable native execution-middleware decorator with effective cancellation/timeout semantics. C1 also affects existing chains independently of Polly and should be treated as a core correctness finding.

| Finding | Classification | Required before MVP? |
| --- | --- | --- |
| C1 effective-token loss | Semantic limitation for per-operation integration on affected chains; hard blocker to transparent native middleware support for arbitrary terminal tokens. A lifetime factory is a narrower shutdown workaround. | No on token-correct paths; fix before certifying affected chains. |
| C2 no execution token copy | Hard public-API blocker for forwarding Polly timeout tokens through a native execution decorator. | No; required for that decorator scope. |
| C3 opaque capability boundary | Ergonomic and semantic limitation of an optional wrapper architecture. | No core change recommended. |
| C4 predecessor scheduler escape | Existing execution-context limitation independent of Polly. | No; restrict affinity claims or resolve separately. |
| C5 documentation/diagnostics | Required documentation plus optional enhancements. | Documentation yes; new core diagnostics no. |

### C1 — Effective execution token is discarded at middleware boundaries

- **Affected code:** `PipelineTaskScheduler.ScheduleTerminal`, `ExecuteEnvelope`, `InvokeExecutionCore` in [TaskSchedulerPipeline.cs, lines 153–207](../../TaskFlow/Internal/TaskSchedulerPipeline.cs); the same producer-token construction exists on newer upstream at [lines 264–266](https://github.com/dombrovsky/TaskFlow/blob/4424d181273cba2fdf37ef51c39781accb6d40bd/TaskFlow/Internal/TaskSchedulerPipeline.cs#L264). `TaskSchedulerOperationContext.CancellationToken` is currently documented as the preterminal producer token.
- **Preventing behavior:** terminal execution supplies a potentially linked/replaced token into ExecuteEnvelope, but each middleware context is constructed with `operation.ProducerCancellationToken`, not the incoming execution token. Calling next(context) passes that stale token onward, including to the final delegate. Another downstream middleware would reset any future attempt-token override in the same way.
- **Use case:** sequential TaskFlow with CancellationToken.None and retry execution middleware. Disposal cancels the terminal-linked token, but a naive retry pipeline sees an uncancelable producer token and can continue backoff. Even pass-through execution middleware can make the actual user delegate miss shutdown cancellation.
- **Observed:** a public HoldingMiddleware captured an uncancelable context token after disposal began; shutdown remained pending until the test released it. This was on the supplied checkout, without any integration implementation.
- **Smallest change:** preserve the incoming effective execution token when constructing each execution-stage context and when invoking the next stage. Review completion-context token construction too rather than accidentally reintroducing producer-token resets there. Preserve CallerCancellationToken as the original caller token. Update the context documentation to distinguish effective execution from preterminal producer cancellation.
- **Kind/compatibility:** internal correction plus documentation, with observable behavior change to a shipped token property. Source/binary signatures can remain compatible, but token equality and cancellation behavior change. If retaining the documented preterminal meaning is required, expose a distinct effective execution-token property and propagate it explicitly instead; choose that only after compatibility review. Do not silently claim this is behavior-neutral.
- **Outside Polly:** shutdown-aware middleware, scoped resources, nested execution deadlines, and custom terminals that supply a different token all benefit.
- **Priority:** P1 independent core correctness work; necessary before an unqualified native-decorator shutdown guarantee, not a prerequisite for direct use on an undecorated/token-correct flow.
- **Existing public workaround:** a factory registration can capture `TaskSchedulerMiddlewareLifetime.StoppingToken` and link it into the token supplied to Polly; explicit lifetimes work for custom terminals. This can cancel retry backoff without a new public lifetime API. It does not recover an arbitrary terminal-specific token, nor fix downstream user-token propagation. Therefore lack of a public shutdown token is **not** a separate hard blocker.

### C2 — No public execution-context token replacement

- **Affected code:** [`TaskSchedulerOperationContext`](../../TaskFlow/Middleware/TaskSchedulerOperationContext.cs), [`TaskSchedulerExecutionDelegate<TResult>`](../../TaskFlow/Middleware/TaskSchedulerExecutionDelegate.cs). The context is sealed, constructed internally, immutable, and has no WithCancellationToken counterpart to [`TaskSchedulerEnqueueContext<TResult>.WithCancellationToken`](../../TaskFlow/Middleware/TaskSchedulerEnqueueContext.cs).
- **Preventing behavior:** Polly timeout changes ResilienceContext.CancellationToken, but execution middleware can invoke only continuation(TaskSchedulerOperationContext). It cannot provide the new attempt token through public APIs. Ignoring that token can make timeout ineffective or cause an operation canceled by another token to be misclassified.
- **Use case:** retry wrapping a fresh 100 ms Polly timeout per attempt, with arbitrary downstream middleware and a cooperative user delegate. Each attempt needs a distinct timeout token while retaining the same TaskFlow operation and queue turn.
- **Smallest change:** add an immutable `TaskSchedulerOperationContext.WithCancellationToken(CancellationToken)` copy operation, retaining operation identity, original caller token, state, annotation scope, registration-local slot, and callback scheduler. Ensure C1's stage propagation uses the copy's effective token. No new scheduler interface or Polly-specific hook is needed.
- **Kind/compatibility:** additive public API and a small internal propagation correction. Existing interface implementers need no new method. Copies must not mutate shared operation state, particularly if future orchestration creates more than one execution branch.
- **Outside Polly:** nested deadlines, per-attempt cancellation scopes, custom timeout policies, and arbitrary execution middleware.
- **Priority:** P1 only if the native scheduler decorator is selected; **not required for the recommended MVP**, which wraps the actual operation delegate and passes Polly's token directly.
- **Workaround:** wrap the operation delegate or return an opaque ITaskScheduler wrapper. The first is recommended; the second incurs C3.

### C3 — Opaque wrappers hide native admission/keyed capability

- **Affected code:** [`BoundedAdmissionController.Submit`, lines 37–45](../../TaskFlow/Admission/BoundedAdmissionController.cs), `TaskSchedulerMiddlewareExtensions.GetPipeline`, and upstream [`KeyedTaskSchedulerExtensions.Validate<TKey>`](https://github.com/dombrovsky/TaskFlow/blob/4424d181273cba2fdf37ef51c39781accb6d40bd/TaskFlow/Keyed/KeyedTaskSchedulerExtensions.cs#L61).
- **Use case:** an outer custom resilience scheduler forwards Enqueue correctly, but passing it to the controller's explicit Submit fails because it is not the native pipeline. Keyed Enqueue similarly requires native typed routing. Adding middleware outside the wrapper creates a new boundary; it does not rediscover hidden controllers/coordinators.
- **Classification/remedy:** ergonomic/semantic limitation of the opaque-wrapper architecture, not a missing universal scheduling contract. Use operation wrapping or native middleware. **Propose no core change.** A new capability-forwarding abstraction would have broader compatibility and maintenance costs without an MVP necessity.

### C4 — Configured scheduler can be lost before Polly begins

- **Affected code:** [`TaskFlow.RunAfterPrevious`, line 176](../../TaskFlow/TaskFlow.cs), unlike initial invocation in TaskFlowSchedulerAdapter.
- **Use case:** two queued operations on a custom single-thread TaskScheduler; the second awaits an incomplete predecessor and starts the user delegate on the pool. This was observed without Polly.
- **Smallest candidate change:** preserve the configured scheduling context across the predecessor wait, for example by changing that await's capture behavior. Verify inline/queued paths and scheduler rejection/disposal behavior before selecting the implementation.
- **Kind/compatibility:** internal implementation correction, but observable scheduler/thread placement changes; it needs its own regression tests and release note. It is broadly useful outside Polly.
- **Classification/priority:** P2 independent core investigation; necessary before claiming strict custom-scheduler affinity for this contended execution path, **not required for an MVP that preserves/documentedly inherits current terminal behavior**. This report does not implement or validate the candidate fix.

### C5 — Documentation and optional enhancements

Document policy eligibility, same-position retry, lane occupancy, effective tokens, cancellation skipping the inner callback, callback affinity, and early caller completion versus quiescence. This is MVP work, not a new abstraction. A flow/pipeline diagnostics bridge, named-provider overloads, an operation-level context configurator, per-attempt annotations, and an adapter-local affinity dispatcher are optional enhancements. Existing Polly callbacks already provide useful attempt diagnostics, so no core attempt events are required.

No core change is proposed to make hedging preserve mutual exclusion: overlapping attempts are the strategy's purpose. No new synchronous admission API, graceful-shutdown mode, key coordinator, timer abstraction, or Polly inspection hook is required for resilience integration. #37 and existing TimeProvider support should remain independent work.

## 11. Architecture comparison

| Alternative | Complexity and correctness | API/result support | DI, ownership, tests, maintenance | Core changes |
| --- | --- | --- | --- | --- |
| A. Documentation only: execute Polly inside scheduled delegates | Lowest complexity. Correct one-turn semantics when tokens/context are forwarded correctly; repetitive setup is easy to get wrong. | Full void/typed support through Polly. Caller chooses exactly which operations are resilient. | Pipelines supplied directly/provider-resolved; ownership stays external. Usage recipes need executable coverage. Almost no adapter maintenance. | None on correct delegate paths; existing terminal/middleware limitations still apply. |
| B1. Native execution-middleware scheduler decorator | Natural TaskFlow policy composition, one queue turn, downstream interception per attempt. Current C1/C2 prevent full effective-token semantics. | Nongeneric ResiliencePipeline can handle all result types. ResiliencePipeline<T> is awkward on an untyped ITaskScheduler accepting unrelated types. | Factory lifetime can stop backoff; supplied pipelines remain borrowed. Strong composition test burden; metadata/lifetime/reentrancy need review. | C1/C2 for general support. A retry-only factory can be more restricted today. |
| B2. Opaque scheduler decorator wrapping the submitted delegate | Moderate implementation; can receive/forward actual tokens for ordinary Enqueue and keep one turn. Introduces a composition boundary. | Nongeneric pipeline works across result types; typed pipeline still needs compile-time result discipline. | Works with ordinary DI chain configuration; borrowed ownership. Native Submit/keyed calls do not transparently work through it. More boundary documentation and tests. | None for restricted unkeyed Enqueue; no recommendation to add core forwarding APIs. |
| C. Per-operation integration via resilient delegates | Small implementation, handles the actual delegate token and typed results without scheduler internals. Polly stays inside native scheduling. | Void and typed operations; naturally reused by Enqueue, Submit, and keyed operations. Explicit delegate types resolve overload ambiguity. | Provider-compatible without DI dependency. No adapter-owned resources. Focused tests for pooling, tokens, ordering, context. Some ergonomic benefit over A, limited maintenance. | **None for MVP**, subject to documented current token/context limits. |
| D. Enqueue/completion policies or metadata-selected middleware | Enqueue is appropriate for deliberate pre-admission limiting/breaker checks, not repeated next calls. Completion is useful for final observation, not same-position retry. Annotation selection is possible but adds result-matching rules. | Typed result selection becomes cumbersome; per-operation direct use is clearer initially. | More lifetime/ordering combinations and larger policy surface. Existing interception is observability, not a retry orchestration hook. | No new seam needed for its legitimate uses; C1/C2 still apply to execution orchestration. |

**Initial package recommendation: C, supported by A.** It adds enough value if it centralizes safe context/token handling; otherwise publish A first and postpone packaging. Defer B1 until demonstrated user demand warrants global resilience policy and C1/C2 are resolved. Do not ship B2 as a transparent substitute for native composition.

## 12. Validation performed and remaining questions

No production implementation, dependency declaration, public API baseline, issue, or PR was changed. Scratch repositories, SDK installation, and experiments were under `/tmp`; no experimental source is part of this report commit.

### Observed checks

A temporary .NET 10 console program referenced the supplied TaskFlow project and Polly.Core 8.8.0. It exercised isolated behaviors, not a TaskFlow.Polly implementation:

| Probe | Observed result |
| --- | --- |
| Public execution middleware token after owner shutdown | Token captured from context remained uncancelable; DisposeAsync waited until the held operation was released. |
| Dedicated-thread retry using Polly default context | First and second attempts ran on different threads. |
| Dedicated-thread retry using ContinueOnCapturedContext=true, ordinary delay | Both attempts ran on the same dedicated thread. |
| Same capture setting with asynchronously completing custom DelayGenerator | Second attempt ran on another thread. |
| Direct retry with one-hour backoff and a later FIFO operation | Later operation remained pending during backoff; DisposeAsync interrupted delay within a three-second guard; attempt count stayed at one. |
| Polly timeout with an operation deliberately ignoring cancellation | Pipeline was incomplete when timeout cancellation fired; after release it returned the operation's successful value, 42. |
| Contended TaskFlow on custom single-thread TaskScheduler, without Polly | Second delegate's TaskScheduler.Current differed from the configured scheduler after predecessor waiting. |
| Dedicated-thread Polly hedging with two gated async invocations | Maximum overlapping invocation count was two. |

The console checks ran twice while extending the probes; they are local characterizations, not a cross-framework compatibility suite. Claims about unsupported custom strategies are reasoning, not tested outcomes.

Repository validation on the untouched production checkout: `dotnet restore TaskFlow.sln` succeeded. `dotnet format whitespace TaskFlow.sln --no-restore` completed without tracked production changes. Both `dotnet format whitespace TaskFlow.sln --verify-no-changes --no-restore` and `dotnet format style TaskFlow.sln --diagnostics IDE0161 --verify-no-changes --no-restore` exited successfully; the formatter emitted workspace-load warnings. A focused .NET 10 test selection covering FuturePolicyMiddlewareSeamsFixture, BoundedAdmissionFixture, MiddlewareLifetimeFixture, and TimeoutTaskSchedulerExtensionsFixture ran **97 tests: 96 passed, 1 failed**. The failure was `SharedProducer_DetachesCallerButPreservesOwnershipCancellation(LocalScope)`, whose ActiveCount remained 1 at its 100 ms assertion. No production edits preceded the run. This is an existing-checkout failure observed here; no root cause or flaky-test fix is claimed. The report does not claim the suite is green.

In the separate temporary checkout of upstream 4424d18, the .NET 10 `KeyedSchedulingFixture` run passed **26/26** existing tests. No Polly-specific keyed prototype was executed. Local report checks also verified relative source-link targets, balanced code fences, and whitespace/line-ending compliance. The full solution test suite was not run.

Existing evidence includes `Retry_UsesOneQueueTurnAndInvokesDownstreamOncePerAttempt` (test-only simple retry), `Timeout_DoesNotFreeUnderlyingCapacity`, `Cleanup_WaitsForSubmissions_AttemptsAllInReverseOrder_AndSharesFailure`, and `CurrentThreadAndCustomScheduler_AffinityIsPreserved`. The latter concerns its specific adapter setup and does not disprove the distinct contended TaskFlow predecessor case. On newer upstream, inspect `ReadyKeys_AreRoundRobin_AndEachKeyIsFifo`, `CombinedBudgets_ReserveAtomically_AndSkipBlockedKeys`, `Timeout_DoesNotReleaseKeyOrExecutionSlot`, `Shutdown_ClosesWaiters_CancelsUndispatched_AndWaitsForUnderlyingWork`, and `QueuedWork_PreservesSubmittingExecutionContextAndConfiguredScheduler` in [KeyedSchedulingFixture](https://github.com/dombrovsky/TaskFlow/blob/4424d181273cba2fdf37ef51c39781accb6d40bd/TaskFlow.Tests/Extensions/KeyedSchedulingFixture.cs). None is a Polly integration test.

### Risks and questions for a prototype

- Test per-operation wrapping with existing execution decorators after resolving/characterizing C1. A helper cannot recover a terminal-linked token that upstream middleware already discarded.
- Verify same-key retries, different-key progress, both capacity modes, mixed Wait/Reject producers, shutdown while full, and timeout-ignoring work with Polly. Quiescence-based accounting is promising but needs actual composition tests.
- Verify typed result retries/fallback, exception identity/stack, cancellation token normalization, callback exceptions, and disposal during OnRetry/DelayGenerator. Avoid brittle timing assertions; use gates and each library's TimeProvider support.
- Validate .NET 8/.NET 10 and netstandard2.0 assets, dedicated/current-thread flows, adapter-based custom schedulers, ambient ExecutionContext, and suppressed flow. Only .NET 10 probes ran here.
- Decide whether affinity restrictions are sufficient for the first release or an adapter-local callback dispatcher is worth prototyping. Capturing context does not guarantee callback affinity for every Polly internal path.
- A supplied pipeline can contain nested/custom strategies. Do not pretend the pipeline type certifies safe scheduling semantics. MVP support is a documented contract; runtime strategy detection/enforcement has not been designed or tested. No reflection into Polly internals should be required.
- Retry at-least-once side effects, fallback masking failures, shared breakers across tenants, limiter lock cycles, and long head-of-line waits are application-visible behavior changes even without API breaks.
- Pipeline callbacks must not synchronously dispose the current flow or await work queued behind the current operation. Provider/root disposal order and reload during in-flight retries need end-to-end ownership tests.

## 13. MVP scope and independently deliverable roadmap

The first package should support borrowed preconfigured nongeneric and typed pipelines; token-aware asynchronous void/result operations; one scheduled turn per resilient operation; ordinary retry with backoff/jitter/custom predicates; cooperative timeout; breaker and sequential fallback; and direct provider-resolved pipeline use. Existing native admission/keyed calls should receive resilient delegates without a wrapper around the scheduler. Support shared resource limiters as a documented application-supplied strategy, with no automatic coordination with TaskFlow admission.

Explicitly exclude whole-operation hedging and custom concurrent/detached strategies from scheduling-guarantee claims; automatic provider registration or pipeline construction; owning supplied pipelines; a second admission/retry queue; transparent global typed-pipeline decorators; universal affinity for policy callbacks; and asynchronously completing DelayGenerator on thread-affine flows unless a dispatcher is separately validated. Do not promise that all current execution-middleware chains preserve shutdown cancellation.

1. **Publish executable usage guidance.** Show direct in-delegate Polly calls, pooled context lifetime, token forwarding, same-position retry, timeout ordering, per-attempt telemetry, and unsupported strategy configurations. This can ship without a package.
2. **Resolve core correctness independently.** Characterize C1 with queued and running operations across execution models and custom token-transforming terminals. Decide its compatibility path. Investigate C4 separately; neither change should introduce a Polly dependency.
3. **Prototype the per-operation helper outside production code.** Validate void/typed signatures, native Submit/keyed interoperability, and pooled-context cleanup on success/failure/cancellation. Confirm boilerplate reduction warrants a package.
4. **Ship the small optional package.** Add only demonstrated helper overloads, package README, public API baseline, and meaningful composition tests. Match repository framework and release conventions. No automatic strategy defaults that users must undo.
5. **Add focused advanced validation.** Cover shared limiter/breaker state across keys, provider reload/shutdown, optional metadata correlation, and any affinity dispatcher. Add capabilities only when these tests demonstrate a need.
6. **Reconsider a native scheduler decorator.** If demanded, add C2 and effective-token propagation, then implement B1 with explicit result-type policy and documentation of interceptor order. A retry-only decorator is a narrower option; do not advertise timeout/hedging equivalence just because the accepted object is ResiliencePipeline.

## 14. Recommendation for issue #9

Adjust its scope toward **optional Polly-based retry integration with predictable single-turn semantics**, rather than implementing a second general retry engine in TaskFlow. Keep its desired behavior: eligible failures retry, cancellation/shutdown stop future attempts promptly, terminal failures remain observable, and backoff's effect on later work is explicit.

The periodic synchronization example needs one more distinction: retrying the delegate restarts the whole delegate. A long-lived loop that already performed side effects may need Polly around one loop iteration or resource call instead. That can be expressed directly inside the scheduled delegate without a global scheduler policy.

Keep native TaskFlow timeout, admission, cancellation scopes, and throttle; they express queue/lifetime behavior Polly does not replace. There is no native production retry implementation to remove. Defer any minimal native retry convenience until a concrete requirement demonstrates value beyond documentation plus the optional package. Track general token/affinity corrections independently of Polly. This report recommends an issue update but does not modify issue #9.

## 15. Decision table

| Question | Conclusion | Confidence |
| --- | --- | --- |
| Can integration use current public APIs? | Yes: direct in-delegate/per-operation integration; not arbitrary-pipeline guarantees across all middleware. | High |
| Are core API changes required? | No for MVP. An execution-context token-copy API is needed for a general native timeout-capable decorator. | High |
| Are internal implementation changes required? | No for correct MVP paths. Fix effective-token propagation before certifying all native middleware chains; custom-scheduler predecessor affinity is a separate finding. | High |
| Can strict FIFO ordering be preserved? | Yes within a serial lane/same key when the complete sequential resilience pipeline is awaited in one turn. Re-enqueue retries and overlapping strategies do not meet that contract. | High |
| Is keyed scheduling compatible? | Yes on upstream 4424d18 through native keyed delegates; one reservation/key turn per resilient operation. Polly-specific accounting tests remain required. | Medium-high |
| Are dedicated-thread guarantees preserved? | Conditionally: context capture and sequential strategies; exclude async DelayGenerator escape and do not promise affinity for breaker transition callbacks. Hedging violates non-overlap. | High for observed cases |
| Is a scheduler decorator appropriate? | A future native execution decorator is natural after token fixes; prefer per-operation integration for the MVP. | High |
| Should issue #9 change scope? | Yes: optional Polly integration and documented retry/lifetime semantics; no existing native retry implementation needs replacement. | High |