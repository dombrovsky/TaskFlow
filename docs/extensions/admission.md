---
layout: page
title: Bounded admission
permalink: /extensions/admission/
---

# Bounded admission

`WithBoundedAdmission` limits accepted operations without blocking producers. The unconfigured flow remains unbounded. The policy is available on every TaskFlow target framework.

```csharp
await using var flow = new TaskFlow();
var admission = new BoundedAdmissionController(new BoundedAdmissionOptions
{
    Capacity = 32,
    CapacityMode = TaskSchedulerCapacityMode.Outstanding,
    AdmissionMode = TaskSchedulerAdmissionMode.Wait,
    MaxAdmissionWaiters = 128
});

ITaskScheduler scheduler = flow
    .WithBoundedAdmission(admission)
    .WithTimeout(TimeSpan.FromSeconds(10))
    .WithOperationName("save");

await scheduler.Enqueue(token => SaveAsync(token));
```

The original flow owns the registration. Keep and dispose that flow; the scheduler and controller do not become disposable owners. Options are captured when the controller is constructed. The options-only overload creates a controller internally when explicit admission decisions are unnecessary.

## Capacity and waiting

- `Outstanding` counts accepted operations, including running work, until the underlying pipeline work settles. Capacity 2 permits one running and one queued operation on a sequential flow.
- `Pending` counts accepted operations until their user delegate first starts. Capacity 2 permits two queued operations in addition to running work. An execution middleware delay still counts as pending. Retrying a delegate does not consume another reservation.
- Capacity must be positive. Pending capacity can conservatively reserve a slot for a submission that is about to start; this API does not offer a zero-buffer rendezvous.
- `Wait` is the default admission mode. Full registrations retain submissions outside the execution lane and admit them in FIFO order.
- `Reject` does not wait for capacity. Existing admission waiters cannot be overtaken.
- `MaxAdmissionWaiters` is optional and nonnegative. A full waiter queue rejects new waiting submissions. Zero disables retaining capacity waiters.

Sequential submissions enter the controller in call order. Concurrent submissions are ordered by entry into its synchronized admission controller. One dispatcher invokes admitted continuations in that order, rather than relying on the order in which asynchronous waiters resume. Downstream custom middleware that deliberately delays, coalesces, or reorders work retains its own semantics.

Capacity belongs to a registration. Descendant snapshots share its gate. Independent registrations, including registrations created from the same controller on separate branches, have independent capacity. Submitting directly to the original flow or through a branch without this registration bypasses its limit.

Outstanding capacity does not bound unlimited admission waiters. Await submissions in producers to apply backpressure. To bound retained submissions through a registration, also configure `MaxAdmissionWaiters`; handle overflow instead of repeatedly retrying or spawning more producers.

## Immediate load shedding or asynchronous backpressure

| API | Capacity unavailable | Decision | Middleware |
| --- | --- | --- | --- |
| `TrySubmit(...)` | Immediately returns `Full`; never registers an admission waiter | Synchronous enum | Validated native pipelines described below |
| `Submit(..., Wait)` | Waits asynchronously and cancellably | `Admission` task | Existing native middleware pipeline |
| `Submit(..., Reject)` | Rejects when the admission stage is reached | `Admission` task; upstream middleware may delay it | Existing native middleware pipeline |
| `Enqueue(...)` | Uses configured admission mode | Operation completion task | Existing pipeline behavior |

Use `TrySubmit` when a synchronous event producer must decide whether to consume a cooldown, acknowledge an event, or shed load before returning. The controller exposes this through extension methods, without changing `ITaskScheduler`:

```csharp
// A deliberately supported branch without the earlier timeout enqueue middleware.
ITaskScheduler immediate = flow.WithBoundedAdmission(admission);
Task Save(CancellationToken token) => SaveAsync(token);
var attempt = admission.TrySubmit(immediate, Save, cancellationToken);
if (attempt.Admission == TaskSchedulerAdmissionResult.Accepted)
{
    await attempt.Completion!;
}
else
{
    // Full or Closed; Completion is null and there is no rejection task to observe.
    ReportRejected(attempt.Admission);
}
```

`TaskSchedulerTrySubmission` (and its generic result form) contains the immediate `Admission` enum and a reusable `Completion` task **only on acceptance**. Observe every accepted completion for success, cancellation, and failure. Internal rejection failures, including failures introduced by completion middleware, are observed by the scheduler. Rejection completion middleware still runs and remains lifetime-owned; it cannot change the already returned admission decision.

`Accepted` atomically reserves every applicable coordinated global/per-key budget before returning. It does not promise execution success. Dispatch is deferred off the producer's stack, including with an inline custom .NET `TaskScheduler`; execution may begin concurrently before `TrySubmit` returns. No asynchronous work is synchronously waited on. Synchronization uses the existing admission locks; this is not a lock-free or hard real-time API.

An already canceled caller token throws `OperationCanceledException` synchronously without submitting work. Cancellation racing admission either wins before admission (the call throws) or applies to accepted work through its completion task. A closed lifetime returns `Closed` with null completion. `TrySubmit` never returns `WaiterLimitReached`, because it never registers a waiter. Earlier eligible admission waiters retain priority.

Convenience overloads accept synchronous delegates and `Task`/`ValueTask` delegates, with and without results or delegate token parameters. Keyed delegates receive `key` or `(key, token)`. Typed-state core overloads accept `ValueTask` delegates, with or without results; wrap a named `Task` operation in `new ValueTask(...)` or `new ValueTask<T>(...)` when using typed state. Use named delegates or explicit lambda return types if an async lambda is ambiguous between Task and ValueTask overloads. See the [synchronous event producer recipe](../recipes.md#shed-load-from-synchronous-events) for a cooldown example.

### Dispatch and producer context

Admission and dispatch are separate phases. Each admission domain has one drain owner, claimed under its existing queue lock before a thread-pool worker is queued. Further arrivals join that drain. The keyed coordinator schedules a drain only when work can run; arrivals blocked by a busy key or execution limit need no worker. Completion, cancellation, and capacity release recheck runnable work. Publishing idle uses the same lock as queue mutation, so an arrival either joins the current drain or starts the next one.

The worker is queued without capturing a producer's `ExecutionContext`. Each operation restores its own captured context when it dispatches; submissions made with flow suppressed execute without inheriting another producer's ambient values. Lifetime ownership is reestablished even when flow was suppressed, including around a custom `TaskScheduler.QueueTask`. A legacy inline `Enqueue` drain hands off before reaching a `TrySubmit` operation or an operation whose context flow was suppressed. Execution and completion middleware keep their usual scheduler-context behavior.

This coalesces dispatch work items, but does not eliminate terminal scheduling or asynchronous task continuations. Work that becomes runnable only after a previous drain has retired needs another drain. Rejection or pre-execution cancellation with completion middleware still uses a separate per-operation asynchronous handoff to protect the producer from those callbacks; without completion middleware, rejection needs no such handoff. There is no dedicated thread or polling loop. See the [reproducible dispatch measurements](https://github.com/dombrovsky/TaskFlow/tree/main/benchmarks/Dispatch).

The off-stack guarantee covers operation delegates, supported middleware callbacks, and terminal `QueueTask` calls initiated by this submission. It does not make admission wait-free: existing locks can contend, and keyed hashing/equality must run synchronously to decide admission. Supply a fast, stable, nonblocking, nonthrowing comparer. Runtime `ExecutionContext`/`AsyncLocal` notifications also retain their .NET behavior. With a custom backend that blocks inside `QueueTask`, a direct native flow can hold its existing owner lock and delay a concurrent producer entering that lifetime. Prefer a keyed/concurrent branch, or `TaskFlowSchedulerAdapter` with an explicit lifetime, for such backends. No asynchronous capacity or execution task is synchronously waited on.

### Dispatch failures and progress

Operation, execution/completion middleware, and custom terminal scheduling exceptions settle the affected accepted completion; they do not abandon the shared drain. Worker submission uses the runtime thread pool, whose managed queue has no capacity-rejection decision. Progress and shutdown draining require a functioning thread pool and cooperative running work. Resource exhaustion, runtime termination, forced thread abort, or a host without thread-pool support are outside this liveness guarantee. In particular, queueing can fail after the runtime has already published the callback, so blindly clearing drain ownership and retrying cannot safely recover. The scheduler does not fall back to executing callbacks on the producer. See the [dispatch ownership review](https://github.com/dombrovsky/TaskFlow/blob/main/benchmarks/Dispatch/REVIEW.md) for the runtime-source audit and fault tests.

### Supported TrySubmit pipelines

Capability validation runs before operation creation, middleware invocation, or capacity reservation:

- A native pipeline must contain exactly one registration of the selected controller. Missing/duplicate pairing throws `ArgumentException`; null arguments throw `ArgumentNullException`.
- One uncoordinated bounded registration works on built-in `TaskFlow`, `DedicatedThreadTaskFlow`, `CurrentThreadTaskFlow`, or `TaskFlowSchedulerAdapter` terminals. An adapter needs an explicit middleware lifetime. Opaque/custom `ITaskScheduler` terminals throw `NotSupportedException`.
- Keyed or concurrent branches support multiple coordinated global/per-key budgets and execution limits. Configure the branch before adding admission. Multiple independent uncoordinated bounded registrations throw `NotSupportedException`, since they cannot reserve atomically together.
- Annotations and execution/completion middleware are supported at any position, including error handlers and execution interceptors.
- Any other **enqueue middleware**, at any position, throws `NotSupportedException`. This includes custom enqueue middleware even when it currently forwards synchronously, and built-in timeout, throttling, cancellation-scope, cancel-previous, and logging middleware that implement enqueue interception. A middleware object implementing multiple phases is unsupported if one is enqueue.

No middleware is silently bypassed. Keep `Submit(..., Reject)` for these broader compositions: it still traverses enqueue middleware, and rejects capacity immediately once it reaches admission. `Submit(..., Wait)` continues to provide asynchronous backpressure. To use `TrySubmit`, compose a supported branch deliberately; moving unsupported enqueue middleware inside the admission stage does not make it supported.

## Admission separately from completion

`Admission` and `Completion` remain reusable `Task` handles, including for synchronous decisions. Retaining them, awaiting them multiple times, and combining them with `Task.WhenAll` is supported.

`Enqueue` uses the registration's configured default mode and returns the caller-facing operation task. `BoundedAdmissionController.Submit` traverses the same pipeline and gate, overrides the mode for that controller's registration, and exposes two tasks:

```csharp
var submission = admission.Submit(
    scheduler,
    token => SaveAsync(token),
    TaskSchedulerAdmissionMode.Reject,
    cancellationToken);

var decision = await submission.Admission;
if (decision == TaskSchedulerAdmissionResult.Accepted)
{
    await submission.Completion;
}
else
{
    // Completion also faults with AdmissionRejectedException; observe its rejection.
    try { await submission.Completion; }
    catch (AdmissionRejectedException rejection) { ReportRejected(rejection.Result); }
}
```

`Admission` reports `Accepted`, `Full`, `WaiterLimitReached`, or `Closed`. Accepted means the selected registration reserved capacity, not that downstream policies or scheduling succeeded. The delegate may later succeed, throw, or cancel independently. Rejections fault `Completion` with `AdmissionRejectedException`; normal `Enqueue` observes the same exception. Completion middleware can still transform caller-facing outcomes according to its existing contract.

Cancellation before admission cancels both tasks and prevents delegate invocation. Once admitted, cancellation follows the terminal scheduler's existing behavior: built-in flows still invoke accepted queued delegates with a possibly canceled token. Cancellation racing acceptance either wins before reservation or follows these accepted-work semantics.

If upstream middleware fails or cancels without reaching the selected registration, `Admission` propagates that failure or cancellation after underlying pipeline work settles. A caller-facing timeout alone does not settle admission while inner middleware could still reach the registration. A successful upstream short circuit without reaching the registration faults `Admission` with `InvalidOperationException`. The pipeline records the final caller-facing outcome after completion middleware runs, then settles an unreached admission before leaving the owning lifetime. Admission bookkeeping does not await or observe the returned `Completion` task; callers remain responsible for observing its failures. Submitting after lifetime entry has closed faults with `ObjectDisposedException` before a registration can decide; waiters already inside the registration receive `Closed`.

`Reject` means no capacity waiting when the registration is reached. Other enqueue middleware may wait before reaching it, so `Admission` is not guaranteed to be settled before `Submit` returns. The submission call does not synchronously wait for the decision.

Convenience overloads support synchronous delegates, `Task`, and `ValueTask`. As with `Enqueue`, use named asynchronous delegates with an explicit return type or a cast if an async lambda is ambiguous between Task and ValueTask overloads.

The supplied scheduler must be a native TaskFlow pipeline containing exactly one registration of this controller. Missing or ambiguous pairing throws `ArgumentException`. Later native decorators preserve the override and caller state. Opaque third-party wrappers do not expose their internal registrations, so they cannot be supplied to controller `Submit`. With several controllers, the override targets only the selected controller; all other registrations use their configured defaults.

## Timeouts and execution completion

A timeout or caller-facing cancellation does not prove underlying work stopped. Outstanding reservations remain occupied until that work settles. Pending reservations remain occupied until the delegate actually starts, even if its caller-facing task timed out while queued. If execution never starts, the reservation is released when the pipeline work settles. Delegate failures, scheduling failures, and cancellation release reservations exactly once.

Decorator position retains meaning. In the example above, timeout includes admission waiting. With `.WithTimeout(timeout).WithBoundedAdmission(admission)`, the bounded policy waits first and the timeout begins after admission. Moving the policy does not lose the `Submit` API.

Never await another operation queued onto the same sequential lane from its running operation. Bounded admission does not remove that deadlock restriction.

## Lifetime and shutdown

Flow disposal atomically closes registration and pipeline entry, signals middleware stopping, drains terminal work and tracked pipeline work, then attempts owned cleanup in reverse registration order. This includes pre-terminal admission waiters, short circuits, and underlying work whose caller already timed out. Admission waiting uses the stopping signal; accepted work retains terminal cancellation semantics.

Repeated disposal shares the shutdown operation. Operation failures remain observable on their returned tasks. Cancellation-callback and resource-cleanup failures are aggregated after all shutdown phases and cleanups have been attempted. Synchronous disposal retains its existing timeout; cleanup continues if that timeout expires. Self-disposal from the owner's submission, factory, or cleanup throws `InvalidOperationException` to avoid waiting on itself.

Factories implement `ITaskSchedulerMiddlewareFactory.Create(lifetime)`. Each call creates a fresh registration instance, which may implement `IAsyncDisposable` or `IDisposable`. Existing `UseMiddleware(middleware)` remains caller-owned, including disposable objects shared across flows. Factory implementations must return independently owned state and settle active submissions in response to `StoppingToken`; cleanup runs after quiescence and cannot be required to unblock work.

A factory racing shutdown is either rejected before creation or attached to the owner and cleaned up even if registration cannot return a usable snapshot. Appending middleware shares existing registrations and creates only the appended instance.

Non-flow terminals need an explicitly owned lifetime:

```csharp
var terminal = new TaskFlowSchedulerAdapter(TaskScheduler.Default);
await using var lifetime = new TaskSchedulerMiddlewareLifetime();
var scheduler = terminal.WithBoundedAdmission(admission, lifetime);
await scheduler.Enqueue(() => Save());
```

Dispose the explicit lifetime before disposing its terminal. TaskFlow does not infer ownership through opaque wrappers or permit replacing an existing pipeline lifetime. Custom terminal schedulers must accurately represent the work they schedule through their delegate and task contracts.

## Keyed and concurrent branches

Configure keyed/concurrent execution before bounded admission to combine all capacity budgets atomically. `WithPerKeyBoundedAdmission` reuses the same controller/options within each key scope; keyed `Submit` reports the combined decision. See [Keyed scheduling](keyed-scheduling.md) for setup, fairness, timeout placement, and shutdown behavior.