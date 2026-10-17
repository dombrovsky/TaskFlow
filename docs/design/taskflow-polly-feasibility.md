---
layout: page
title: TaskFlow.Polly feasibility and architecture
permalink: /design/taskflow-polly-feasibility/
---

# TaskFlow.Polly feasibility and architecture

Initial research: 2026-10-16; independent review follow-up: 2026-10-17. This is a design investigation, not an integration implementation or an API commitment.

## 1. Executive summary

**Polly integration is feasible, but the revised recommendation is documentation-only direct usage now. A package containing only per-operation delegate helpers is not justified by the evidence available.** Polly already accepts the required delegates/tokens, pools execution contexts, supports typed results, and integrates with DI. Compile-checked examples in the Independent Review Follow-up show that ordinary, keyed, and DI usage is already compact.

If TaskFlow decides to offer centrally configured resilience policies, prefer a native **scheduler.WithResilience(ResiliencePipeline)** execution-middleware decorator after a coherent C1/C2 fix. Its value is cross-cutting policy configuration and native composition, not unique Polly functionality or an established performance improvement. Small general-purpose core changes can support it without adding Polly to TaskFlow or extending ITaskScheduler. Typed result-specific policies can remain direct per-operation Polly calls; defer a hybrid API.

C1 is an existing correctness problem: execution middleware substitutes the preterminal producer token for the terminal's effective execution token, potentially losing shutdown cancellation. Correct the propagation while explicitly reviewing the shipped token property's semantics; preserve distinct caller and producer information. C2 is the missing immutable execution-context token copy needed to pass per-attempt timeout tokens downstream. C4 independently violates documented custom-TaskScheduler callback placement after an incomplete predecessor wait. These findings deserve separate, dependency-free core work. The intermittent ownership test has a separate cleanup-synchronization race; it does not exercise C1.

No arbitrary ResiliencePipeline guarantees sequential attempts or affinity. Hedging can overlap callbacks; Polly 8.8.0's asynchronous DelayGenerator can escape capture; breaker transition callbacks use their own background executor. Neither direct usage nor a package should promise otherwise. Existing native admission, timeout, cancellation, and ownership semantics remain TaskFlow responsibilities. There is no production native retry implementation to replace, and issue #9 was not modified.

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

## 4. Polly APIs and candidate package designs

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

### Original per-operation candidate (not the revised package recommendation)

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

This candidate removes repeated explicit execution-context setup when that setup is needed. Follow-up investigation found that Polly already pools contexts in its token overloads and permits a builder-level pool to select capture defaults. Most direct calls need only a scheduled ExecuteAsync lambda. That weakens the justification for a separate helper package; see the compile-checked examples and allocation measurements in the Independent Review Follow-up.

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

The current execution-middleware token defect changes this picture: adding even pass-through execution middleware can replace the terminal-linked token with a preterminal token before the user delegate sees it. See finding C1. Therefore the no-core-change direct-use claim assumes the scheduled delegate actually receives the intended token. Do not certify shutdown preservation across arbitrary current middleware chains; characterize those chains or fix C1 independently.

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

**No core change is needed for direct Polly usage on token-correct paths; no helper-only MVP package is now recommended.** C1/C2 are requirements for the preferred future native execution decorator, not blanket blockers to all integration architectures. The follow-up below designs them together, including compatibility and completion semantics. C1 and C4 are existing correctness findings independent of Polly.

| Finding | Classification | Dependency of revised recommendation |
| --- | --- | --- |
| C1 effective-token loss | Semantic limitation for per-operation integration on affected chains; hard blocker to transparent native middleware support for arbitrary terminal tokens. A lifetime factory is a narrower shutdown workaround. | No for direct token-correct paths; fix before certifying affected chains or a general native decorator. |
| C2 no execution token copy | Hard public-API blocker for forwarding Polly timeout tokens through a native execution decorator. | Required for the future native decorator; not for direct usage. |
| C3 opaque capability boundary | Ergonomic and semantic limitation of an optional wrapper architecture. | No core change recommended. |
| C4 predecessor scheduler escape | Existing execution-context limitation independent of Polly. | Existing documented-contract bug; resolve before promising custom-flow scheduler affinity. |
| C5 documentation/diagnostics | Required documentation plus optional enhancements. | Documentation yes; new core diagnostics no. |

### C1 — Effective execution token is discarded at middleware boundaries

- **Affected code:** `PipelineTaskScheduler.ScheduleTerminal`, `ExecuteEnvelope`, `InvokeExecutionCore` in [TaskSchedulerPipeline.cs, lines 153–207](../../TaskFlow/Internal/TaskSchedulerPipeline.cs); the same producer-token construction exists on newer upstream at [lines 264–266](https://github.com/dombrovsky/TaskFlow/blob/4424d181273cba2fdf37ef51c39781accb6d40bd/TaskFlow/Internal/TaskSchedulerPipeline.cs#L264). `TaskSchedulerOperationContext.CancellationToken` is currently documented as the preterminal producer token.
- **Preventing behavior:** terminal execution supplies a potentially linked/replaced token into ExecuteEnvelope, but each middleware context is constructed with `operation.ProducerCancellationToken`, not the incoming execution token. Calling next(context) passes that stale token onward, including to the final delegate. Another downstream middleware would reset any future attempt-token override in the same way.
- **Use case:** sequential TaskFlow with CancellationToken.None and retry execution middleware. Disposal cancels the terminal-linked token, but a naive retry pipeline sees an uncancelable producer token and can continue backoff. Even pass-through execution middleware can make the actual user delegate miss shutdown cancellation.
- **Observed:** a public HoldingMiddleware captured an uncancelable context token after disposal began; shutdown remained pending until the test released it. This was on the supplied checkout, without any integration implementation.
- **Smallest change:** preserve the incoming effective execution token when constructing each execution-stage context and when invoking the next stage. Review completion-context token construction too rather than accidentally reintroducing producer-token resets there. Preserve CallerCancellationToken as the original caller token. Update the context documentation to distinguish effective execution from preterminal producer cancellation.
- **Kind/compatibility:** internal correction plus documentation, with observable behavior change to a shipped token property. Source/binary signatures can remain compatible, but token equality and cancellation behavior change. If retaining the documented preterminal meaning is required, expose a distinct effective execution-token property and propagate it explicitly instead; choose that only after compatibility review. Do not silently claim this is behavior-neutral.
- **Outside Polly:** shutdown-aware middleware, scoped resources, nested execution deadlines, and custom terminals that supply a different token all benefit.
- **Priority:** P0 independent core correctness work; necessary before an unqualified native-decorator shutdown guarantee, not a prerequisite for direct use on an undecorated/token-correct flow. The follow-up recommends an explicit ProducerCancellationToken property and a documented behavioral compatibility decision.
- **Existing public workaround:** a factory registration can capture `TaskSchedulerMiddlewareLifetime.StoppingToken` and link it into the token supplied to Polly; explicit lifetimes work for custom terminals. This can cancel retry backoff without a new public lifetime API. It does not recover an arbitrary terminal-specific token, nor fix downstream user-token propagation. Therefore lack of a public shutdown token is **not** a separate hard blocker.

### C2 — No public execution-context token replacement

- **Affected code:** [`TaskSchedulerOperationContext`](../../TaskFlow/Middleware/TaskSchedulerOperationContext.cs), [`TaskSchedulerExecutionDelegate<TResult>`](../../TaskFlow/Middleware/TaskSchedulerExecutionDelegate.cs). The context is sealed, constructed internally, immutable, and has no WithCancellationToken counterpart to [`TaskSchedulerEnqueueContext<TResult>.WithCancellationToken`](../../TaskFlow/Middleware/TaskSchedulerEnqueueContext.cs).
- **Preventing behavior:** Polly timeout changes ResilienceContext.CancellationToken, but execution middleware can invoke only continuation(TaskSchedulerOperationContext). It cannot provide the new attempt token through public APIs. Ignoring that token can make timeout ineffective or cause an operation canceled by another token to be misclassified.
- **Use case:** retry wrapping a fresh 100 ms Polly timeout per attempt, with arbitrary downstream middleware and a cooperative user delegate. Each attempt needs a distinct timeout token while retaining the same TaskFlow operation and queue turn.
- **Smallest change:** add an immutable `TaskSchedulerOperationContext.WithCancellationToken(CancellationToken)` copy operation, retaining operation identity, original caller token, state, annotation scope, registration-local slot, and callback scheduler. Ensure C1's stage propagation uses the copy's effective token. No new scheduler interface or Polly-specific hook is needed.
- **Kind/compatibility:** additive public API and a small internal propagation correction. Existing interface implementers need no new method. Copies must not mutate shared operation state, particularly if future orchestration creates more than one execution branch.
- **Outside Polly:** nested deadlines, per-attempt cancellation scopes, custom timeout policies, and arbitrary execution middleware.
- **Priority:** P1 if the native scheduler decorator is selected; not required for direct per-operation Polly calls. No helper-only package is now recommended.
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
- **Classification/priority:** P0 correction of documented scheduler placement; necessary before claiming strict custom-scheduler affinity for this contended execution path. It is independent of Polly and does not block direct use on other execution models. The follow-up isolates the capturing-await mechanism experimentally; no core fix was implemented or validated end to end.

### C5 — Documentation and optional enhancements

Document policy eligibility, same-position retry, lane occupancy, effective tokens, cancellation skipping the inner callback, callback affinity, and early caller completion versus quiescence. This is required documentation work, not a new abstraction. A flow/pipeline diagnostics bridge, named-provider overloads, an operation-level context configurator, per-attempt annotations, and an adapter-local affinity dispatcher are optional enhancements. Existing Polly callbacks already provide useful attempt diagnostics, so no core attempt events are required.

No core change is proposed to make hedging preserve mutual exclusion: overlapping attempts are the strategy's purpose. No new synchronous admission API, graceful-shutdown mode, key coordinator, timer abstraction, or Polly inspection hook is required for resilience integration. #37 and existing TimeProvider support should remain independent work.

## 11. Architecture comparison

| Alternative | Complexity and correctness | API/result support | DI, ownership, tests, maintenance | Core changes |
| --- | --- | --- | --- | --- |
| Documentation only: execute Polly inside scheduled delegates | Lowest complexity. Correct one-turn semantics when tokens/context are forwarded correctly; repetitive setup is easy to get wrong. | Full void/typed support through Polly. Caller chooses exactly which operations are resilient. | Pipelines supplied directly/provider-resolved; ownership stays external. Usage recipes need executable coverage. Almost no adapter maintenance. | None on correct delegate paths; existing terminal/middleware limitations still apply. |
| Native execution-middleware scheduler decorator | Natural TaskFlow policy composition, one queue turn, downstream interception per attempt. Current C1/C2 prevent full effective-token semantics. | Nongeneric ResiliencePipeline can handle all result types. ResiliencePipeline<T> is awkward on an untyped ITaskScheduler accepting unrelated types. | Factory lifetime can stop backoff; supplied pipelines remain borrowed. Strong composition test burden; metadata/lifetime/reentrancy need review. | C1/C2 for general support. A retry-only factory can be more restricted today. |
| Opaque scheduler decorator wrapping the submitted delegate | Moderate implementation; can receive/forward actual tokens for ordinary Enqueue and keep one turn. Introduces a composition boundary. | Nongeneric pipeline works across result types; typed pipeline still needs compile-time result discipline. | Works with ordinary DI chain configuration; borrowed ownership. Native Submit/keyed calls do not transparently work through it. More boundary documentation and tests. | None for restricted unkeyed Enqueue; no recommendation to add core forwarding APIs. |
| Per-operation integration via resilient delegates | Small implementation, handles the actual delegate token and typed results without scheduler internals. Polly stays inside native scheduling. | Void and typed operations; naturally reused by Enqueue, Submit, and keyed operations. Explicit delegate types resolve overload ambiguity. | Provider-compatible without DI dependency. No adapter-owned resources. Focused tests for pooling, tokens, ordering, context. Some ergonomic benefit over A, limited maintenance. | None for this delegate path, subject to documented current token/context limits; a helper-only package is not now recommended. |
| Enqueue/completion policies or metadata-selected middleware | Enqueue is appropriate for deliberate pre-admission limiting/breaker checks, not repeated next calls. Completion is useful for final observation, not same-position retry. Annotation selection is possible but adds result-matching rules. | Typed result selection becomes cumbersome; per-operation direct use is clearer initially. | More lifetime/ordering combinations and larger policy surface. Existing interception is observability, not a retry orchestration hook. | No new seam needed for its legitimate uses; C1/C2 still apply to execution orchestration. |

**Revised recommendation: direct usage now; a native execution decorator if a package is later justified.** The original per-operation-first package recommendation was challenged and withdrawn. The Independent Review Follow-up compares the requested A–D choices with compile-checked consumer examples. An opaque wrapper remains inappropriate as a transparent replacement for native composition.

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

Initial 2026-10-16 validation on the untouched production checkout: `dotnet restore TaskFlow.sln` succeeded. `dotnet format whitespace TaskFlow.sln --no-restore` completed without tracked production changes. Both `dotnet format whitespace TaskFlow.sln --verify-no-changes --no-restore` and `dotnet format style TaskFlow.sln --diagnostics IDE0161 --verify-no-changes --no-restore` exited successfully; the formatter emitted workspace-load warnings. A focused .NET 10 test selection covering FuturePolicyMiddlewareSeamsFixture, BoundedAdmissionFixture, MiddlewareLifetimeFixture, and TimeoutTaskSchedulerExtensionsFixture ran **97 tests: 96 passed, 1 failed**. The failure was `SharedProducer_DetachesCallerButPreservesOwnershipCancellation(LocalScope)`, whose ActiveCount remained 1 at its 100 ms assertion. No production edits preceded the run. This is an existing-checkout failure observed here; no root cause or flaky-test fix is claimed. The report does not claim the suite is green.

In the initial investigation's separate temporary checkout of upstream 4424d18, the .NET 10 `KeyedSchedulingFixture` run passed **26/26** existing tests. That initial pass did not execute a Polly-specific keyed prototype; the follow-up below executes direct Polly calls on successful keyed/admission paths, but not faulting/retrying keyed compositions. Local report checks also verified relative source-link targets, balanced code fences, and whitespace/line-ending compliance. The full solution test suite was not run.

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

## 13. Scope and independently deliverable roadmap

The immediate deliverable should be executable documentation using direct Polly calls. Do not publish only delegate helpers: their unique benefit is insufficiently demonstrated. If centrally configured flow/branch resilience is desired, a future TaskFlow.Polly package should expose a native execution decorator for borrowed ResiliencePipeline instances. That pipeline can execute void and generic operations; typed result-specific pipelines remain available directly inside selected delegates.

The future decorator must use one existing tracked native submission, propagate effective attempt tokens, await the entire supported sequential pipeline, keep provider/pipeline ownership external, and retain native admission/keyed routing. Its supported configuration contract must be explicit. Exclude whole-operation concurrent hedging, detached/custom parallel strategies, universal policy-callback affinity, and unvalidated automatic context restoration. Do not invent a second queue or lifecycle owner. A typed global scheduler abstraction, hybrid helper overloads, metadata enrichment, and custom context factories are optional later work.

1. **Publish compile-checked direct-use recipes.** Cover ordinary/keyed/admission calls, DI, context capture, timeout ordering, and telemetry. Document current token/scheduler limitations.
2. **Address existing C1/C4 correctness independently.** Characterize the effective-token and configured-scheduler contracts and select a documented compatibility path. Keep TaskFlow dependency-free.
3. **If a native package is selected, complete C1/C2 coherently.** Add the minimal token-copy/producer-observation surface, correct all execution/completion stage propagation, and cover scoped replacement and shutdown.
4. **Validate the actual native integration in a future implementation task.** Consumer compilation is not execution validation. Use gated tests for retries, per-attempt timeout, keyed/admission accounting, interception ordering, exception preservation, and cancellation/disposal.
5. **Gate package publication on cross-cutting configuration value.** Start with the small nongeneric-pipeline scheduler extension and documented strategy eligibility. No automatic pipeline construction or supplied-resource ownership.
6. **Evaluate optional capabilities separately.** Provider reload races, custom context factories, an affinity dispatcher, typed conveniences, or a hybrid surface require their own demonstrated need and tests.

## 14. Recommendation for issue #9

Adjust its scope toward **documented Polly-based retry with predictable single-turn semantics**, and a native optional package only if central flow/branch configuration is desired, rather than implementing a second general retry engine in TaskFlow. Keep its desired behavior: eligible failures retry, cancellation/shutdown stop future attempts promptly, terminal failures remain observable, and backoff's effect on later work is explicit.

The periodic synchronization example needs one more distinction: retrying the delegate restarts the whole delegate. A long-lived loop that already performed side effects may need Polly around one loop iteration or resource call instead. That can be expressed directly inside the scheduled delegate without a global scheduler policy.

Keep native TaskFlow timeout, admission, cancellation scopes, and throttle; they express queue/lifetime behavior Polly does not replace. There is no native production retry implementation to remove. Defer any minimal native retry convenience until a concrete requirement demonstrates value beyond documentation plus the optional package. Track general token/affinity corrections independently of Polly. This report recommends an issue update but does not modify issue #9.

## Independent Review Follow-up

Follow-up date: 2026-10-17. This section revises the initial recommendation after independent review. It uses the same TaskFlow and Polly revisions identified above. Only this report changes in the working repository. Consumer examples and experiments were compiled in temporary projects; no production integration, core fix, or dispatcher was implemented.

### Revised decision: documentation now; native decorator if a package is pursued

**Do not publish a package containing only the proposed delegate helpers. Recommend direct Polly usage now. If TaskFlow elects to provide a supported, centrally configured resilience package, prefer a native `scheduler.WithResilience(ResiliencePipeline)` execution decorator after the coherent C1/C2 change below.** That decorator is feasible with small general-purpose core changes. It has more compelling value than the initially recommended helper package: one policy registration covers all relevant operations, integrates with chain configuration, and can position execution interceptors inside or outside attempts without hiding admission/keyed capabilities.

The research establishes technical feasibility, not sufficient demand to incur package maintenance. #9 can initially be served by executable documentation. A later decorator package is justified by demand for centrally enforced per-flow policies and supported composition, not by a claim that Polly execution requires TaskFlow helpers. Do not start with a hybrid surface or a new typed scheduler abstraction. Typed result policies can remain direct Polly calls inside selected operations.

| Criterion | A. Direct Polly | B. Delegate helpers | C. Native scheduler decorator after fixes | D. Hybrid |
| --- | --- | --- | --- | --- |
| Real-world fit | Best for distinct policies by operation/resource, or retrying one call inside a long loop. | Useful with repeatedly reused delegates and borrowed pipelines; less useful with local method groups. | Best when every operation submitted through a configured branch needs the same exception-based policy. Reduces the chance a caller forgets it. | Covers both patterns, but two ways to attach resilience increase documentation and composition choices. |
| Boilerplate eliminated | A token-aware scheduled lambda is often the entire bridge. Explicit context setup is needed only when per-execution configuration is needed. | Removes that lambda and, when needed, explicit context/finally code; introduces delegate declarations or casts. | Removes every repeated per-call bridge and centralizes context, metadata, and attempt/interceptor ordering. | Same benefits as C plus selected B conveniences; risks accidental nested retry multiplication. |
| API/discoverability | Existing ExecuteAsync is documented and searchable. No additional API vocabulary. | Extension discovery requires a delegate receiver; a method group often must first be assigned to a typed Func. Task/ValueTask/key/state combinations enlarge the surface. | WithResilience fits existing WithTimeout/WithLogging chains. One nongeneric pipeline overload serves void and all result types. | Highest overload count and naming ambiguity. A typed per-operation escape hatch already exists in Polly. |
| DI configuration | Inject provider/pipeline into the component or configure its pool once on the builder. No TaskFlow-specific DI layer. | Same provider lookup, plus wrapping at each operation definition. Cannot centrally decorate all submissions through AddTaskFlow by itself. | Existing configureSchedulerChain can resolve one borrowed provider pipeline once per configured flow/branch. No new DI package dependency is necessary. | More flexible, but service registration must explain which policy is global and which is local. |
| Generic/nongeneric operations | Full Polly support, including typed result predicates/fallback and void callbacks. | Needs multiple helper overloads; no new result capability over Polly. | ResiliencePipeline handles void and generic execution. Do not accept a global ResiliencePipeline<T> over unrelated result types; typed fallback/result policies stay per operation. | Broadest convenience, with extra type-selection rules and tests. |
| Native keyed/admission APIs | Native scheduler remains visible. | Native scheduler remains visible. | UseMiddleware preserves the native pipeline and its routing/controller metadata. | Preserved if the scheduler side is native middleware, not an opaque wrapper. |
| Lifetime/tokens/context | Correct on token-correct paths when the delegate's supplied token is used. Caller owns policy configuration. Existing C1/C4 still matter. | Cannot repair a token or scheduler already lost upstream. Pooling/capture convenience does not certify strategy behavior. | C1/C2 make effective attempt-token propagation possible within one tracked envelope. Terminal lifetime remains authoritative; borrowed pipelines need no new owner. Strategy/context restrictions still apply. | Adds opportunities to mix nested deadlines, retry loops, and instrumentation scopes. Same restrictions as C. |
| NuGet justification | None needed. | Too thin on the evidence available. No demonstrated performance advantage. | Plausible for a maintained cross-cutting policy facility with documented guarantees and integration tests. Gate publication on that requirement. | Defer until actual usage demonstrates the extra helpers are valuable. |

The earlier recommendation understated C's usability and overstated B's unique contribution. It remains correct that A/B can work through public APIs, that arbitrary pipelines cannot be certified safe, and that no new scheduler interface is needed.

### Compile-checked consumer examples

The examples were compiled with the .NET 10 SDK, Polly.Extensions/Polly.Core 8.8.0, Microsoft.Extensions.DependencyInjection 10.0.0, and TaskFlow upstream 4424d18. A's ordinary, keyed, native Submit, named DI, and scoped keyed DI paths were also executed successfully. **B–D calls compiled against throwing declarations of the proposed extension signatures; they were not executed and do not prove the unimplemented decorator or C1/C2 fix.** This separates consumer API usability from implementation correctness. All nine C# fences in this follow-up were then extracted verbatim into methods/classes with the shared declarations and candidate-signature stubs; the final compilation succeeded with zero warnings and errors.

Shared declarations for these fragments are `record Order(string Id)`, `ReadAsync(CancellationToken)` returning `ValueTask<Order>`, `ReadAsync(string, CancellationToken)` returning `ValueTask<Order>`, and `SaveAsync(CancellationToken)` returning `ValueTask`. Pipelines are preconfigured and borrowed; the samples use System.Threading.Tasks.Flow, Polly, Polly.Registry, and Microsoft.Extensions.DependencyInjection namespaces.

A — ordinary operations require no adapter helper:

```csharp
await using var flow = new TaskFlow();
Order first = await flow.Enqueue(ct => common.ExecuteAsync(ReadAsync, ct));
Order second = await flow.Enqueue(ct => typed.ExecuteAsync(ReadAsync, ct));
await flow.Enqueue(ct => common.ExecuteAsync(SaveAsync, ct));
```

Here `common` is ResiliencePipeline and `typed` is ResiliencePipeline<Order>. The lambdas return ValueTask/ValueTask<Order> directly, avoiding the ambiguity of an untyped value-returning async lambda. Async method groups and cancellation forwarding are already supported by Polly. For a keyed resource and explicit admission, the native API also stays compact:

```csharp
await using var owner = new TaskFlow();
var admission = new BoundedAdmissionController(
    new BoundedAdmissionOptions { Capacity = 16 });
var scheduler = owner.WithKeyedScheduling<string>()
    .WithConcurrencyLimit(2).WithBoundedAdmission(admission);
Func<string, CancellationToken, ValueTask<Order>> operation =
    (key, ct) => typed.ExecuteAsync(ReadAsync, key, ct);

var submission = admission.Submit(scheduler, "document-17", operation,
    TaskSchedulerAdmissionMode.Wait, CancellationToken.None);
await submission.Admission;
Order result = await submission.Completion;
Order next = await scheduler.Enqueue(
    "document-17", operation, CancellationToken.None);
```

A — existing DI configuration can supply typed pipelines without a bridge package:

```csharp
var services = new ServiceCollection();
services.AddResiliencePipeline<string, Order>("read", builder =>
    builder.AddRetry(new Polly.Retry.RetryStrategyOptions<Order>
    {
        MaxRetryAttempts = 2,
        Delay = TimeSpan.FromMilliseconds(10)
    }));
services.AddTaskFlow("storage");
await using var root = services.BuildServiceProvider();
var pipeline = root.GetRequiredService<ResiliencePipelineProvider<string>>()
    .GetPipeline<Order>("read");
await using var flow = root.GetRequiredService<ITaskFlowFactory>()
    .CreateTaskFlow("storage");
Order order = await flow.Enqueue(ct => pipeline.ExecuteAsync(ReadAsync, ct));
```

B — proposed helpers remove some repeated context code but can add a typed delegate declaration:

```csharp
Func<CancellationToken, ValueTask<Order>> read = ReadAsync;
Func<CancellationToken, ValueTask> save = SaveAsync;
var resilientRead = read.WithResilience(typed);
await scheduler.Enqueue(resilientRead, CancellationToken.None);
await scheduler.Enqueue(save.WithResilience(common), CancellationToken.None);
await keyedScheduler.Enqueue("document-17", resilientRead, CancellationToken.None);
```

The compiled B surface includes the three signatures from section 4: void operation/nongeneric pipeline, result operation/nongeneric pipeline, and result operation/typed pipeline. A component can resolve `pipelines.GetPipeline<Order>("read")` and pass it to the helper; that is ordinary injection, not an additional DI capability. State/key-aware helper overloads would require further design or closures. An overloaded keyed ReadAsync method group produced CS0121 during consumer checks; an explicitly typed Func resolved it. No helper should claim to eliminate every overload ambiguity.

C — preferred API **if a package is built**:

```csharp
ITaskScheduler resilient = owner.WithResilience(common);
await resilient.Enqueue(ReadAsync, CancellationToken.None);
await resilient.Enqueue(SaveAsync, CancellationToken.None);

ITaskScheduler keyed = owner.WithKeyedScheduling<string>()
    .WithConcurrencyLimit(2).WithBoundedAdmission(admission)
    .WithResilience(common);
Func<CancellationToken, ValueTask<Order>> read = ReadAsync;
await keyed.Enqueue("document-17", read, CancellationToken.None);
await admission.Submit(keyed, "document-17", read,
    TaskSchedulerAdmissionMode.Wait, CancellationToken.None).Completion;
```

One proposed extension, `ITaskScheduler WithResilience(this ITaskScheduler, ResiliencePipeline)`, suffices for those calls. It must append an execution middleware registration and return the resulting native snapshot. It must not return a new opaque ITaskScheduler wrapper. No typed global scheduler overload is proposed. Ordinary flow-chain DI configuration is equally direct:

```csharp
services.AddResiliencePipeline("storage", builder =>
    builder.AddRetry(new Polly.Retry.RetryStrategyOptions { MaxRetryAttempts = 2 }));
services.AddTaskFlow("storage", configureSchedulerChain: (scheduler, provider) =>
    scheduler.WithResilience(provider
        .GetRequiredService<ResiliencePipelineProvider<string>>()
        .GetPipeline("storage")));
```

D — the hybrid's distinct benefit is combining a global exception policy with a typed local policy:

```csharp
Func<CancellationToken, ValueTask<Order>> read = ReadAsync;
await scheduler.WithResilience(globalBreaker)
    .Enqueue(read.WithResilience(typedRetry), CancellationToken.None);
```

This compiled shape does not require the hybrid helper: direct `typedRetry.ExecuteAsync` inside the delegate expresses the local policy today. Do not accidentally put equivalent retries in both pipelines and multiply attempts. The DI branch is the C registration plus a provider-resolved typed pipeline in the consumer.

**New DI compatibility finding:** named TaskFlow factory results are ownership wrappers. In an executed example, configuring `AddTaskFlow("keyed", configureSchedulerChain: (s, _) => s.WithKeyedScheduling<string>())`, then calling keyed Enqueue on the returned ITaskFlow, failed with “A matching native keyed pipeline is required.” Explicit controller Submit likewise requires the native pipeline, not a factory ownership wrapper. See [TaskFlowFactory.CreateTaskFlow](../../TaskFlow.Extensions.Microsoft.DependencyInjection/TaskFlowFactory.cs), [TaskFlowOwnershipWrapper](../../TaskFlow.Extensions.Microsoft.DependencyInjection/Internal/TaskFlowOwnershipWrapper.cs), and [TaskFlowWrapper](../../TaskFlow.Extensions.Microsoft.DependencyInjection/Internal/TaskFlowWrapper.cs). Ordinary Enqueue still reaches the configured chain, so this does not prevent C's ordinary DI configuration.

A current public-API workaround, also compiled and executed, is explicit scoped registration of the owner and native snapshot:

```csharp
services.AddScoped<TaskFlow>();
services.AddScoped<ITaskScheduler>(provider =>
    provider.GetRequiredService<TaskFlow>()
        .WithKeyedScheduling<string>().WithConcurrencyLimit(2));
```

Resolve the scheduler within an async DI scope; that scope disposes its separately registered TaskFlow owner. Polly's provider remains borrowed. Configure bounded admission or the future native decorator on this same native snapshot. This finding needs documentation; it is not a demonstrated need for a new capability-forwarding interface or a hard blocker to DI support.

### What an adapter actually adds, including measurements

The initial report's pooling rationale was incomplete. [`ResiliencePipeline.GetAsyncContext<TResult>`](https://github.com/App-vNext/Polly/blob/8.8.0/src/Polly.Core/ResiliencePipeline.AsyncT.cs#L196) obtains a context from its configured pool, and token-taking ExecuteAsync overloads return it in finally. **Explicit pooling in an adapter duplicates existing functionality; it is not inherently an efficiency improvement.** Token forwarding, typed pipelines, exception preservation, and provider lookup also exist already.

Context capture is a real configuration issue, but it need not require per-operation helpers. A caller controlling pipeline construction can set `ResiliencePipelineBuilderBase.ContextPool` once. This application configuration was compiled and exercised with ordinary dedicated-thread retry; both attempts stayed on the same thread:

```csharp
sealed class CaptureContextPool : ResilienceContextPool
{
    public override ResilienceContext Get(ResilienceContextCreationArguments a) =>
        Shared.Get(a.OperationKey, a.ContinueOnCapturedContext ?? true,
            a.CancellationToken);
    public override void Return(ResilienceContext context) => Shared.Return(context);
}
```

Assign `ContextPool = new CaptureContextPool()` on the builder, including inside AddResiliencePipeline configuration. It changes the default only when the execution did not explicitly choose a capture setting. It does not fix Polly's asynchronous DelayGenerator escape or arbitrary strategy behavior. For an already-built borrowed pipeline whose builder/pool cannot be changed, explicitly supplying a pooled execution context remains useful, but is normal Polly usage.

A helper/decorator that always supplies its own context also **bypasses the supplied pipeline's configured ContextPool** and its context defaults. The pool is internal to ResiliencePipeline, so an external adapter cannot automatically discover and reuse it. Choose and document this behavior: a first native decorator could own execution-context creation using Shared/capture=true while preserving the supplied strategies/state, but cannot claim it preserves every custom-pool default. A context-factory overload is optional, only if a concrete custom-pool use case warrants it. A token-overload implementation instead honors the pipeline pool and leaves capture configuration to the application. Neither choice proves universal affinity.

Allocation characterization used Release .NET 10, one retry strategy with a callback succeeding on its first attempt, a synchronously completed ValueTask<int> callback returning 42, 30,000 warm-ups, then seven rounds of 200,000 executions per variant. Each measured ValueTask was checked to be synchronously successful. `GC.GetAllocatedBytesForCurrentThread` measured the callback execution path, excluding pipeline/delegate construction and TaskFlow scheduling:

| Execution path | Median managed bytes per execution |
| --- | --- |
| Nongeneric pipeline, token overload | 24.00 |
| Same nongeneric pipeline, explicit pooled capture=true context | 24.00 |
| Typed int pipeline, token overload | 0.00 |
| Same typed int pipeline, explicit pooled capture=true context | 0.00 |

The 24-byte result is consistent with value-type boxing through RetryStrategyOptions<object> and [`BridgeComponentBase.ConvertOutcome`](https://github.com/App-vNext/Polly/blob/8.8.0/src/Polly.Core/Utils/Pipeline/BridgeComponentBase.cs); it is not pooling overhead removed by an adapter. This small local experiment is not a package benchmark. Timings varied materially between rounds; no throughput/speedup conclusion is justified. Async retry/fault paths, delegate construction, TaskFlow scheduling, custom pools, and an unimplemented adapter were not benchmarked. B/C may introduce closures, state objects, and copy-context allocations; no allocation advantage is claimed for them. C's justification is cross-cutting policy ergonomics, not efficiency.

### C1/C2 as one cancellation contract

Use four distinct terms:

| Token | Origin and required meaning |
| --- | --- |
| C: CallerCancellationToken | Original enqueue caller token. Never replaced or implicitly reattached after enqueue middleware deliberately detaches it. |
| P: ProducerCancellationToken | Final token selected by enqueue middleware for terminal submission. It can differ from C. Before a terminal submission exists, the current implementation initializes this field to C; do not represent it as a finalized producer token on a short-circuit path. |
| E: effective terminal execution token | Token actually supplied by the terminal when it invokes the envelope. For built-in serial flows this links P to shutdown; a custom terminal may supply a different token. E is not derivable in general from C and P. |
| A: effective attempt token | Token selected for a particular downstream invocation, often a Polly timeout token linked to E. Nested execution middleware may further transform A. |

The current pipeline captures P in ScheduleTerminal but discards E when it constructs a middleware context in InvokeExecutionCore. The follow-up trace used an enqueue replacement plus a custom terminal supplying a linked token. Two execution middleware stages and completion all reported P; the actual user callback also received P instead of E. CallerCancellationToken remained C. This demonstrates C1 independently of disposal timing or Polly.

**Semantics decision:** CancellationToken should mean the current phase's effective token. That matches the token received by the downstream operation and the purpose of execution middleware. However, changing the property from its documented P meaning is a **behavioral compatibility change**, not merely an additive method or a harmless documentation correction. Retain explicit access to P with a new ProducerCancellationToken property. C, P, and E/A must not be conflated.

An alternative with strict preservation of the old property meaning is to retain CancellationToken as legacy P, add ExecutionCancellationToken, and add WithExecutionCancellationToken. It can fix user-delegate propagation by making continuations use the new property. It is feasible, but leaves old execution middleware consulting a misleading CancellationToken while next(context) uses another token, and perpetuates differences between final/registration contexts. Prefer coherent effective-token semantics plus an explicit producer property at a documented compatibility boundary. If the project cannot accept that behavioral change in its release policy, choose the additive alternative deliberately; do not silently reinterpret a shipped API. No evidence here establishes that all downstream consumers tolerate the preferred change.

**Minimal proposed change set, not implemented:**

1. In TaskSchedulerOperationContext, define CancellationToken as effective for the current invocation, add ProducerCancellationToken with the P semantics above, and add WithCancellationToken(CancellationToken). The copy retains the exact operation object, captured annotation scope, registration index/local-state slot, callback scheduler, caller token, and producer token. Snapshot P when creating a context and preserve that snapshot in copies; do not make an already-created context follow the mutable operation field if an early caller timeout races a later terminal submission. Do not create a new PipelineOperation, new admission request, or new local-state array.
2. In PipelineTaskScheduler.InvokeExecutionCore, construct every registration's context from the incoming `cancellationToken` argument instead of operation.ProducerCancellationToken. Its continuation must pass `nextContext.CancellationToken` onward, including through intervening pass-through middleware. The final operation and ContextOperationMiddleware must receive that effective token. ScheduleTerminal still records P; ExecuteEnvelope receives E from the terminal.
3. In InvokeCompletion, likewise construct contexts from that invocation's token argument. ExecuteEnvelope already supplies its root E to completion; do not store the last attempt token in shared operation state or pass a disposed attempt scope into completion. Completion middleware can explicitly copy/transform its own downstream token, with the same scoped rules.
4. On the enqueue-side completion winner in EnqueueWithCompletion, supply the best-defined producer token P (or its existing C initialization if terminal submission never occurred). This preserves the current observable producer-token behavior for early completion; there is no terminal execution token to promise on a purely preterminal failure. Do not synthesize E, pretend this is terminal-affine, or silently reattach canceled caller C to intentionally detached producer work.
5. Update the operation-context XML docs, interception-token docs, middleware guidance, public API manifest, and compatibility notes together. TaskSchedulerInterceptionContext currently copies operationContext.CancellationToken, so interception behavior changes too. Existing middleware that compares token identity, expects P, or performs cancellation-sensitive completion work needs explicit review.

WithCancellationToken should be a **non-mutating replacement**, like its enqueue counterpart. It does not create, link, or dispose sources. Middleware supplying a token is responsible for preserving its incoming cancellation when that is required. Polly timeout does so by linking its new token to the incoming context token. Automatically linking every copy to C would break caller-detached/shared-producer semantics; automatically allocating a new source for every copy is unnecessary. Middleware can always ignore cancellation entirely, so this API must not advertise non-removable shutdown enforcement against arbitrary user code.

Override precedence is lexically downstream: A passes a copy carrying token X; B observes X and can pass a copy carrying Y; the user sees Y for that invocation. After B returns, A's original context still carries its original token. A later attempt starts from A's own context/current Polly token, not the previous attempt's canceled token. Repeated sequential continuation calls retain one logical identity and registration-local state. Concurrent calls are not serialized by copying; hedging remains outside the supported contract. Internal validation that a passed context belongs to the same operation is a useful guard; do not introduce a new public operation-ID abstraction just for this.

The native Polly bridge can then execute a supplied pipeline inside InvokeAsync<TResult>, seed Polly from context.CancellationToken (E), and invoke its execution continuation with `context.WithCancellationToken(pollyContext.CancellationToken)` for each attempt. Polly timeout introduces A, downstream stages preserve it, and completion receives root E after the pipeline settles. One submission, one reservation identity, repeated execution, borrowed pipeline, and normal exception propagation all fit the existing architecture. With C1 corrected, a stateless borrowed-pipeline execution middleware needs no factory solely to discover shutdown: E already carries the terminal's cancellation contract.

This is a design argument, not an executed native integration. Consumer-call compilation proves neither token propagation nor quiescence. Required regression tests before acceptance:

- Distinct C/P/E tokens from a custom terminal; zero, one, and multiple execution registrations; the actual delegate receives E and observers can still inspect C/P separately.
- Producer detachment via enqueue middleware: caller cancellation does not cancel the detached producer, while terminal/owner cancellation reaches all stages and the user callback.
- Built-in TaskFlow, dedicated/current-thread flows, adapter/custom terminals: shutdown during a middleware await and during a running delegate, without relying on a wall-clock cleanup assertion.
- Nested linked execution token replacements X/Y separated by pass-through middleware; copy state/annotation reference equality and slot isolation; repeated attempts with fresh timeout sources and no canceled-token leakage.
- Same operation reused downstream, but another operation's context rejected or explicitly documented invalid; no new admission/lifetime identity created by copies.
- Completion after success/failure/cancellation gets E, not the last attempt A. Early rejection/timeout gets the defined P/C fallback, runs once, and does not promise terminal affinity. Completion token transformations survive subsequent stages and callback-error fallback.
- Polly retry → timeout and timeout → retry, cancellation during backoff, exception/callback replacement, pipeline result recovery, and actual-completion admission accounting for both capacity modes and keyed execution.

C2 is a hard limitation only for **forwarding a replacement token through this exact native execution-continuation API today**: the input context is sealed, its constructor is internal, its token is get-only, and the continuation has no token argument. There is no public way for a middleware to construct a same-operation context with a new token. A lifetime factory can supply stopping to Polly but cannot communicate a new per-attempt token to downstream execution. A/B remain correct architectural workarounds on token-correct paths, and an opaque wrapper works for restricted ordinary Enqueue. Thus C1/C2 are requirements for C, not universal hard blockers to any Polly integration.

### The ownership test failure is a separate synchronization race

Twenty isolated runs of the **unchanged** `SharedProducer_DetachesCallerButPreservesOwnershipCancellation` test method executed both cases:

| Case | Passes | Failures |
| --- | --- | --- |
| LocalScope | 16 | 4 |
| SharedLane | 11 | 9 |

Every failure was the same `ActiveCount == 0` assertion after 100 ms, not a missing cancellation assertion or an additional producer invocation. This is reproducible intermittency, not evidence that the original single failure was harmless. It also is **not C1**: the test uses only OwnershipSingleFlightMiddleware and DropProducerTokenMiddleware in the enqueue phase, with an inline terminal. InvokeExecutionCore never constructs an execution-middleware context on this path.

The relevant test prototype deliberately separates two events. OwnershipSingleFlightMiddleware.Wait links each caller wait to owner/lane cancellation using Task.WaitAsync. That wait can cancel before Produce's awaited terminal operation and its finally remove the dictionary entry. CancellationTokenSource.CancelAsync waits for cancellation callbacks, not all asynchronous operation continuations. Therefore awaiting the follower's cancellation does not establish producer cleanup/quiescence.

In a separate temporary checkout, only the test fixture/prototype was changed: its producer finally signaled a TaskCompletionSource after removing the entry; the assertion awaited that signal with a three-second guard instead of synchronously polling at 100 ms. Twenty further runs passed both cases (40/40), with production sources unchanged. A separate harness reusing the original private middleware types also observed eventual cleanup in all 60 executions across asynchronous polling and two worker-minimum settings. It did not reproduce a persistent leaked producer. These controls support a test synchronization/timing race sensitive to continuation scheduling; they do not establish a particular OS/thread-pool defect. No owner-token loss was demonstrated on this path.

Recommend an explicit producer-settled signal in the test-only prototype and asynchronous waiting for it. Do not enlarge production cancellation APIs or claim C1 is fixed merely because a longer timeout passes. Rare scheduling failures beyond these samples remain unexcluded.

### C4 and precise execution-context contracts

These are separate properties, not synonyms:

- **Logical FIFO:** which accepted operation holds the lane and which may follow it.
- **Non-overlapping callbacks:** whether two user invocations can remain active simultaneously, even across awaits on one thread.
- **Thread affinity:** the physical thread on which a callback executes.
- **SynchronizationContext affinity:** the captured context object used for asynchronous continuation placement.
- **TaskScheduler.Current affinity:** the scheduler exposed while running a task; one scheduler can use several threads.
- **ExecutionContext flow:** AsyncLocal and other ambient state; it does not by itself move code to a thread or scheduler.

| Model | Actual contract and limitation |
| --- | --- |
| DedicatedThreadTaskFlow | One owned thread, sequential operation envelopes, per-operation synchronization-context pump for captured async continuations. TaskScheduler.Current can still be Default; it is not a special TaskScheduler guarantee. Explicit context escapes/custom parallel policy can violate user-callback affinity/non-overlap. |
| CurrentThreadTaskFlow | The same pumping/sequential model on the thread running Run. The application owns that thread and must allow the pump to progress. |
| TaskFlow with custom TaskScheduler | XML says tasks execute on the configured scheduler; FIFO persists. C4 violates callback-entry placement after a pending predecessor wait. User continuations after their own ConfigureAwait(false) are a separate, intentional escape. |
| TaskFlowSchedulerAdapter | Initially invokes the callback on the supplied TaskScheduler; it adds no logical FIFO or operation-exclusion guarantee. A single-thread TaskScheduler does not itself prevent overlapping async operations. Captured user awaits can return to it. |
| Keyed scheduling | Same-key FIFO/non-overlap of supported sequential operation envelopes; different keys can run concurrently. The branch invokes through TaskFlowSchedulerAdapter over owner Options.TaskScheduler and preserves submitting ExecutionContext in its tested paths. It is not supported on dedicated/current-thread owners. A custom scheduler might itself serialize different keys' synchronous fragments. |

C4 is a **correctness bug relative to documented callback scheduling**, not just a Polly optimization opportunity. TaskFlow.RunAfterPrevious starts on Options.TaskScheduler but awaits an incomplete predecessor with ConfigureAwait(false); the callback is invoked after that await. The earlier experiment demonstrated the configured scheduler being lost. The final ConfigureAwait(false) after invoking the user callback does not cause that initial-placement bug and need not be changed indiscriminately.

The smallest candidate is to capture context at the predecessor await. An isolated reproduction of that await algorithm on a single-thread TaskScheduler, without editing TaskFlow, observed:

| Predecessor await | TaskScheduler.Current correct at callback entry? | Correct after a capturing user await? |
| --- | --- | --- |
| ConfigureAwait(false) | No | No |
| ConfigureAwait(true) | Yes | Yes |

Both variants completed under a three-second guard; the capturing version allowed the scheduler thread to process other queued work rather than synchronously blocking. This validates the candidate mechanism, not a full core patch. It adds scheduler/context dispatch on pending waits and can expose broken schedulers that stop processing prematurely. No production performance comparison was performed.

Alternatives are explicitly scheduling the callback after predecessor completion on Options.TaskScheduler, or rebuilding the chain with scheduler-targeted continuations. They can state scheduler placement more explicitly but add continuations, exception/cancellation plumbing, and rejection/disposal races. Prefer the one-await correction unless tests reveal a scheduler/ambient-context precedence case it cannot satisfy. ConfigureAwait(true) follows standard .NET precedence: a meaningful SynchronizationContext takes precedence over TaskScheduler.Current. Test schedulers that install contexts before claiming both affinities simultaneously.

Do not use synchronous GetResult/Wait to preserve affinity; it can deadlock a one-thread scheduler. Do not use Task.Run, which avoids a blockage by violating the selected scheduler. Awaiting work enqueued behind the current operation remains a logical lane deadlock independent of C4. Synchronously disposing or blocking the scheduler from its own servicing thread is not made safe by either version. Regression coverage should include completed/pending/faulted predecessors, a non-inlining one-thread scheduler, a synchronization-context scheduler, scheduler rejection, shutdown, and suppressed ExecutionContext.

**Restoring an attempt's context is not the same as scheduling it correctly.** A temporary dedicated-thread experiment captured ExecutionContext, entered a pool worker, and called ExecutionContext.Run: AsyncLocal returned to its captured value, but the physical thread and SynchronizationContext were not restored. Setting SynchronizationContext.Current to the captured pump object made object equality true while the callback still ran on the wrong physical thread. Neither is a correct affinity fix for an escaped DelayGenerator.

A bounded design could capture the thread flow's actual per-operation SynchronizationContext at pipeline entry and post each attempt back to it, awaiting completion; a custom TaskScheduler-only entry could schedule on that scheduler instead. Its feasibility follows from the existing thread-flow pump and scheduler APIs, not from an implemented dispatcher. No dispatcher was implemented here. The envelope must remain pending so its pump is alive; posting through TaskFlowSynchronizationContext, which enqueues another operation on the occupied lane, can deadlock. Arbitrary/custom contexts need not represent a dedicated thread, and AsyncLocal restoration must not accidentally freeze every attempt to an obsolete snapshot or erase pipeline callback changes. The adapter cannot infer the intended original scheduler if C4 already lost it before entry.

Therefore automatic universal restoration is **not** a promised feature. Restrict affinity support to tested configurations initially. An adapter-local dispatcher is optional research; it would not make concurrent hedging safe and could not move Polly's independently scheduled circuit transition callbacks onto the lane without those callbacks explicitly cooperating.

### Truthful strategy compatibility contract

Accepting a ResiliencePipeline certifies no strategy topology. Do not inspect private internals or advertise that the type proves sequential attempts. A future API should promise one native scheduled envelope, correct context/token forwarding, borrowed ownership, and awaiting the pipeline's completion. It must explicitly condition callback non-overlap and affinity on pipeline/application behavior. Custom strategies that detach work or return before callbacks settle violate that condition; an adapter cannot generically infer their hidden work.

| Status | Configurations and guarantee boundary |
| --- | --- |
| Locally exercised | Direct ordinary retry, dedicated-thread retry with default/capturing contexts, custom asynchronous DelayGenerator escape, cooperative timeout behavior with ignored cancellation, hedging overlap, context-pool capture defaults, and C4/context distinctions. These are characterization experiments, not a released support suite. |
| Appropriate initial supported-and-tested targets | Sequential retry with built-in delays/jitter and synchronous delay generation; cooperative execution timeout; exception-based breaker; sequential fallback; carefully shared rate/concurrency limits. Each needs actual native-adapter tests before being called supported by a package. |
| Conditional | Async ShouldHandle/OnRetry/OnTimeout/fallback/limiter callbacks must cooperate with cancellation and obey any requested affinity. Polly awaits many of these with its capture flag, but application code can escape internally. Async DelayGenerator in 8.8.0 cannot have a dedicated-thread guarantee without separately validated marshaling. Custom context pools and nested pipelines need explicit context-creation policy. |
| Unsupported scheduling-guarantee configurations | Whole-operation concurrent hedging, custom concurrent/detached strategies, async-void work, blocking callbacks that require the occupied lane, and blanket thread-affinity promises for every supplied pipeline. A hedging configuration with sequential delay is not automatically certified by its strategy name. |
| Entirely application-controlled | Idempotency, which failures/results are handled, resource sharing across keys, fallback meaning, callback duration and context escapes, use of independent external hedged requests, and Polly breaker transition callbacks. The latter use Polly's background executor even when operation context capture is enabled. |

Ordinary asynchronous retry callbacks do not all have DelayGenerator's internal ConfigureAwait(false) problem: ShouldHandle and OnRetry are awaited with context.ContinueOnCapturedContext in the inspected strategy. That preserves the retry engine's resumption context when it entered those awaits on the lane, but does not constrain the callback's own awaits. Do not list all async callbacks as unsafe merely because one generator path escapes, or list all of them as guaranteed safe because they return ValueTask.

### Priorities, dependencies, and remaining evidence gaps

| Priority | Proposed work | Dependency and compatibility impact |
| --- | --- | --- |
| **P0 — existing correctness** | Fix C1's loss of terminal execution cancellation through pass-through middleware. | Existing shutdown behavior can change merely by adding instrumentation. Characterize first; settle effective-token/property compatibility explicitly. This merits prompt correction independent of any package. |
| **P0 — existing correctness** | Correct C4's loss of documented configured-scheduler callback placement. | Prioritize for consumers relying on scheduler affinity. Small candidate, but requires its own tests and behavior-change note; no Polly dependency. |
| **P1 — before native TaskFlow.Polly** | Complete coherent C1/C2 API: effective CancellationToken, explicit ProducerCancellationToken, immutable token copy and stage propagation; define completion fallback and scoped overrides. | Needed for the recommended future native decorator's general timeout/retry semantics. No new ITaskScheduler members or Polly-specific core concepts. Not required for documentation-only/direct use on correct paths. |
| **P1 — before native TaskFlow.Polly** | Specify supported configurations, context-pool/capture behavior, result scope, ownership, and interceptor ordering; implement gated native composition tests in a future task. | Compile-checked call sites are insufficient. C4 must precede advertised support for contended custom TaskFlow schedulers, or that configuration must be explicitly excluded. |
| **P2 — independent** | Replace the ownership fixture's time-based cleanup assertion with producer-settled synchronization; document named-DI wrapper boundaries and scoped native-snapshot registration. | Test/documentation improvements; no evidence they require new production lifecycle or forwarding abstractions. |
| **Optional** | Hybrid/per-operation convenience, typed-state helper overloads, automatic metadata enrichment, custom context factories, adapter-local attempt dispatcher, and package benchmarks. | Add only after demand and a validated contract. Do not use them to delay core correctness fixes. |

Original conclusions retained: optional dependency isolation is feasible; sequential resilience can retain a queue/key turn; admission should account for one logical operation; TaskFlow/Polly timeout scopes differ; hedging can overlap; pipeline/provider ownership should stay external; there is no production native retry implementation to remove.

Conclusions revised: B is no longer recommended as the initial package; pooling is not unique adapter value; C is a credible small-core-change architecture; C4 is a documented-contract bug rather than just a conservative affinity limitation; C1 needs an explicit compatibility decision and distinct producer-token access; the ownership failure has a separate synchronization explanation; DI compatibility must distinguish ordinary factory flow use from native keyed/Submit capability.

Still unverified: a real C1/C2 or C4 core patch, a native Polly integration, all cross-framework assets, native keyed timeout/retry accounting with Polly, provider reload/disposal under in-flight attempts, hostile/custom strategies, callback dispatcher feasibility under arbitrary contexts, downstream binary-consumer behavioral compatibility, and production workloads/performance. This follow-up ran .NET 10 only. It does not claim these proposed fixes or a package are production-ready.

Follow-up validation: solution restore succeeded. Read-only `dotnet format whitespace TaskFlow.sln --verify-no-changes --no-restore` and `dotnet format style TaskFlow.sln --diagnostics IDE0161 --verify-no-changes --no-restore` both exited successfully, with workspace-load warnings. Local Markdown links, code-fence balance, CRLF, trailing whitespace, and final-newline checks passed. Automatic approval review rejected a repository-wide write formatter because it could rewrite production files contrary to this task's constraint; no workaround was used to run it. No requested research finding depends on running a mutating source formatter.

## 15. Decision table

| Question | Conclusion | Confidence |
| --- | --- | --- |
| Can integration use current public APIs? | Yes: direct Polly inside a correctly token-propagating scheduled delegate; native timeout-capable execution decoration requires C1/C2. | High |
| Are core API changes required? | None for direct usage. Preferred future native decorator needs immutable token-copy and distinct producer-token access, with an explicit compatibility decision. | High |
| Are internal implementation changes required? | C1 is an existing shutdown-token correctness bug; C4 is a documented scheduler-placement bug. Native decoration needs effective-token propagation across stages. | High |
| Can strict FIFO ordering be preserved? | Yes for sequential awaited resilience in one lane/key turn. The pipeline type alone cannot certify non-overlapping callbacks or complete tracking of custom detached work. | High |
| Is keyed scheduling compatible? | Yes through the native snapshot on upstream 4424d18. Direct keyed/admission examples executed; named DI ownership wrappers hide these native capabilities, with a scoped-registration workaround. | High for observed paths |
| Are dedicated-thread guarantees preserved? | Only under an explicit strategy/capture contract. Restoring ExecutionContext or assigning SynchronizationContext does not restore the physical thread. | High |
| Is a scheduler decorator appropriate? | Yes: native execution middleware is the preferred future package architecture after small coherent token fixes; a per-operation-helper-only package is not justified now. | High |
| Should issue #9 change scope? | Favor documented Polly usage and optional centrally configured native integration; no native retry implementation needs removal. Do not update the issue in this task. | High |