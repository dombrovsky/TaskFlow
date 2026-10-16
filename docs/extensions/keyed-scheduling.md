---
layout: page
title: Keyed scheduling
permalink: /extensions/keyed-scheduling/
---

# Keyed scheduling

Serialize operations for the same document, customer, or resource while allowing different keys to execute concurrently. The original `TaskFlow` owns the scheduling branch, its middleware, and shutdown.

```csharp
await using ITaskFlow flow = new TaskFlow();

var global = new BoundedAdmissionController(new BoundedAdmissionOptions
{
    Capacity = 128,
    CapacityMode = TaskSchedulerCapacityMode.Pending,
    MaxAdmissionWaiters = 512
});
var perKey = new BoundedAdmissionController(new BoundedAdmissionOptions
{
    Capacity = 16,
    CapacityMode = TaskSchedulerCapacityMode.Pending
});

ITaskScheduler scheduler = flow
    .WithKeyedScheduling<string>(StringComparer.Ordinal)
    .WithConcurrencyLimit(8)
    .WithBoundedAdmission(global)
    .WithPerKeyBoundedAdmission(perKey)
    .WithTimeout(TimeSpan.FromSeconds(10));

// Named delegates with explicit return types also avoid Task/ValueTask overload ambiguity.
Task Save(string key, CancellationToken token) => repository.SaveAsync(key, token);
await scheduler.Enqueue(documentId, Save);

var submission = global.Submit(
    scheduler, documentId, Save, TaskSchedulerAdmissionMode.Reject);
var decision = await submission.Admission;
// Observe Completion for success, cancellation, failure, or AdmissionRejectedException.
await submission.Completion;
```

## Keys and delegates

Key-first `Enqueue` and controller `Submit` overloads supply immutable submission metadata. A delegate can receive the key, or omit it when it already has everything it needs. Typed-state overloads keep the original state reference visible to middleware:

```csharp
await scheduler.Enqueue(
    documentId,
    static (string key, Repository repository, CancellationToken token) =>
        repository.SaveAndGetVersionAsync(key, token),
    repository,
    cancellationToken);
```

The delegate receives the original submitted key, even when a custom comparer considers it equivalent to another key. The configured key type must match the overload's `TKey` exactly. Keys must be non-null and keep stable equality/hash behavior while work is retained.

Missing keyed configuration, a mismatched key type, or a keyless submission to a keyed pipeline throws synchronously before middleware runs. Duplicate keyed scheduling is rejected. Opaque third-party wrappers cannot carry native keyed submission metadata and are rejected by keyed overloads. Native decorators preserve it independently of caller state and annotations.

## Execution policies

`WithKeyedScheduling<TKey>` provides FIFO within each key and round-robin dispatch among ready keys. An active key cannot dispatch another operation until its underlying pipeline work settles, including delayed execution/completion middleware and work remaining after a caller-facing timeout. Concurrent producers are ordered by admission into the coordinator. Middleware outside the coordinated stage may delay or reorder arrivals.

`WithConcurrencyLimit` independently limits dispatched operations until underlying completion. Work behind a busy key consumes no concurrency slot. It is also usable directly on `TaskFlow` without keys, creating a concurrent branch. Multiple limits compose: dispatch reserves every applicable execution limit together.

These extensions create branches using `TaskFlowOptions.TaskScheduler`, independently of the original flow's sequential lane. Direct submissions to the original flow have no ordering or exclusion relationship with the new branch. Different keys may run concurrently, subject to the configured scheduler's capabilities; asynchronous operations retain the normal scheduler-context behavior. Thread-affine flows and custom/opaque scheduler owners are explicitly unsupported for creating these branches. Configure keyed scheduling before concurrency and bounded admission when composing them.

Descendant snapshots share the keyed domain, existing budgets, and existing execution limits. Appending a policy creates only that new policy's state; it does not mutate earlier snapshots. Independent registrations have independent quotas. Idle key state is removed once all active, queued, and admission-waiting submissions for that key settle. The owner does not cache every key ever used.

## Atomic bounded admission

Both global `WithBoundedAdmission` and `WithPerKeyBoundedAdmission` reuse issue 30's options and controller API. No capacity options belong to keyed scheduling or execution concurrency.

On a coordinated branch, all registered capacity budgets are checked and reserved together. A submission never holds one budget while waiting for another. `Pending` counts accepted operations until their user delegate actually starts; dispatch or an execution middleware delay does not release it. `Outstanding` retains capacity until all underlying pipeline work settles. Each per-key registration has independent accounting for each key, including its configured waiter limit.

Waiting preserves FIFO within each key. The coordinator selects the oldest eligible waiting key-head, skipping keys whose per-key budget is full. This avoids a hot key preventing unrelated work from using available global capacity. A new submission cannot overtake an earlier waiter for its key or an earlier eligible waiter. Admission fairness and round-robin execution are separate decisions.

`Submit` targeting any coordinated controller reports the combined admission decision. Its explicit mode applies to that combined decision: `Reject` never waits for any coordinated capacity budget; `Wait` permits waiting. Ordinary `Enqueue` uses the registered policies' defaults; if any participating budget is configured to reject, the combined stage rejects when it cannot admit immediately. `Accepted` means every coordinated budget was reserved, not that dispatch or execution succeeded. Failure/cancellation before reaching the coordinated stage follows issue 30's unreached-admission contract.

The combined stage is placed at the outermost coordinated policy registration. All participating budgets are evaluated there, rather than introducing independent waits at each decorator. A timeout outside that stage includes admission/dispatch waiting; a timeout inside it starts when the selected operation is dispatched. Moving or appending a coordinated policy can move that stage. Ordinary user middleware retains its existing relative order.

Retained admission waiters remain separate from accepted capacity. Configure a finite global `MaxAdmissionWaiters` for a strict total bound; a per-key bound alone does not bound the number of distinct waiting keys. Producers should await submissions instead of issuing unlimited fire-and-forget work.

## Cancellation and shutdown

Cancellation before admission cancels both submission tasks and never invokes the delegate. Cancellation after admission but before dispatch removes queued work and cancels its completion; its admission remains accepted. After dispatch, cancellation is cooperative. Timeout/caller-facing completion cannot release a key, execution slot, or outstanding reservation while underlying work continues.

Disposing the original flow closes submission/registration entry, rejects admission waiters with `Closed`, cancels accepted work that has not been dispatched without invoking it, and signals dispatched work cooperatively. Dispatch selection and closure serialize under the coordinator lock. Disposal awaits all underlying work and then owned middleware cleanup, including work whose caller already timed out. Repeated disposal shares shutdown; synchronous disposal retains the owner's configured timeout.

Never await another operation queued behind the current operation for the same key. Likewise, work occupying the last concurrency slot must not await another operation needing that slot. A timeout does not resolve these dependencies while underlying work continues.
