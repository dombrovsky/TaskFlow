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

## Admission separately from completion

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

If upstream middleware fails or cancels without reaching the selected registration, `Admission` propagates that failure or cancellation after underlying pipeline work settles. A caller-facing timeout alone does not settle admission while inner middleware could still reach the registration. A successful upstream short circuit without reaching the registration faults `Admission` with `InvalidOperationException`. Submitting after lifetime entry has closed faults with `ObjectDisposedException` before a registration can decide; waiters already inside the registration receive `Closed`.

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
