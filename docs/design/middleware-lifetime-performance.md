# Middleware lifetime performance

PR #53 established the adjacent regression boundary `51d6a20` → `34dc96e`
(PR #34). Its same-run comparisons measured 55–62% slower middleware execution,
4,936 → 7,008 managed bytes for eight layers, and 31% slower drained bursts.
Those measurements identify the merge as a whole, not `AsyncLocal` individually.
The [investigation](https://github.com/dombrovsky/TaskFlow/pull/53) links the full
Actions evidence and downloadable raw artifacts.

## Ownership and quiescence are separate concerns

`TaskSchedulerMiddlewareLifetime.Enter`/`Exit` count accepted pipeline operations
and factory registrations under the lifetime lock. `PipelineOperation` counts
activities, tracks actual delegate start, and runs quiescence callbacks before
exiting the owning lifetime. Neither accounting mechanism needs ambient state.
An explicit lifetime reference on the existing operation replaces the allocated
`Lifetime.Exit` delegate; no new ownership object is needed.

The ambient chain serves `CheckReentrantDisposal` only. Public disposal has no
operation/context argument. Arbitrary middleware, user delegates, factories and
async cleanup can call it, including after `await`, from nested flows, and from
child tasks inheriting `ExecutionContext`. Each owner in the chain must reject
self-disposal before closing entry. Explicit ownership on an operation cannot
identify which operation is calling these public methods. Removing the ambient
mechanism entirely would change this guarantee or require a new caller contract.
`ThreadStatic`, `Task.CurrentId`, and a global active-operation flag do not provide
this logical-call-chain identity.

The entry points are:

- Pipeline submission: guards enqueue middleware, preterminal waits, short circuits,
  and caller-outcome/completion processing.
- `TaskFlow.RunAfterPrevious`: guards direct submissions, including when submission
  suppressed `ExecutionContext` flow. The flow's completion chain already drains
  terminal work; it does not need another pipeline activity counter.
- `ThreadTaskFlow`'s delegate wrapper: establishes the owner on its execution
  thread and retains it across asynchronous continuations. It does not introduce
  new propagation of arbitrary caller ambient values.
- The execution envelope: reestablishes ownership at opaque terminal boundaries,
  including keyed dispatch or explicitly suppressed flow. Same-owner entry is cheap.
- `Register`: counts factory work, reserves registration order before invoking a
  factory, and enters its context. Nested registration and shutdown races retain
  ownership and reverse cleanup order.
- `CleanupAsync`: guards all cleanup callbacks, including after asynchronous cleanup
  resumes. Cleanup errors are still aggregated.

## Allocation and async wrapper attribution

On the measured Linux x64 .NET 10 runtime, the original root `EnterContext` allocates
224 bytes and nested entry 296 bytes. An isolated changing-and-restoring
`AsyncLocal<object>` probe allocates 144 bytes; writing the identical reference
allocates nothing. The original scope also allocates a 32-byte frame, 24-byte
closure, 64-byte delegate and 32-byte action disposable. Root entry adds a 72-byte
execution context/map; restoring a nonempty parent adds another 72 bytes. These
sizes are runtime-specific. The probe records raw observations separately from
whole-operation measurements.

A typed internal scope removes the closure/delegate/disposable. Reentering the
same current owner returns a default scope, avoiding both another frame and
execution-context writes. Distinct nested owners still push and restore frames;
frames are immutable, so captured contexts remain safe across async continuations.
Using the entire `PipelineOperation` as an ambient frame could save a small frame
allocation, but captured child contexts would then retain the user state, delegate,
annotations and registration-local state even after the parent operation settles.
Keeping the ambient node small avoids that retention change. An inherited child
context must also retain the disposal guard after its parent has settled; a mutable
"currently active operation" flag would change that behavior.
The optimized root scope allocates 104 bytes; same-owner nesting allocates zero.
The pipeline's submitting scope and built-in terminal's scope are usually such a
redundant pair. Different owners must remain nested.

PR #34 also introduced `EnqueueWithCompletion`, an async `Task` activity wrapper
at every enqueue level (including the terminal), and async `ValueTask` execution
activity wrappers. These are independent of `AsyncLocal`. `ValueTask` avoids a
heap state machine only when its awaited work completes synchronously. A
suspended async `ValueTask` still needs a state-machine box. Activity counter
updates themselves allocate nothing, but require lock operations.

The optimized pipeline combines outer completion/error processing with the root
activity already held by `EnqueueCore`. A forwarding enqueue middleware returning
exactly its child's tracked task can share that activity. The existing per-call
continuation closure stores the returned task identity; volatile publication covers
continuations invoked on another thread. No task table, pooling, or new tracking
object is introduced. Distinct tasks still get independent async tracking, and
the terminal task is tracked. Synchronous failures still become faulted/canceled
submission tasks. The execution activity wrappers remain: arbitrary execution
middleware can abandon an asynchronous child or invoke a continuation repeatedly.

Tracking cannot simply be removed for unbounded pipelines. Even without admission,
a timeout or short-circuiting middleware can return before underlying work has
quiesced, while owned middleware still needs to remain alive. Capacity reservations
and keyed lane/concurrency reservations are released through the same quiescence
mechanism; pending-only capacity is released at actual delegate start. Admission
caller outcomes are recorded after completion middleware and before root release.

An annotation-only snapshot over a built-in flow has no middleware or coordination
callbacks to drain, so it can forward directly to that flow. This bypass retains
asynchronous exception delivery. The optimization excludes opaque/custom terminals
and never bypasses a middleware registration, admission request or coordinator.
Bare flows retain their ambient guard: dropping it would permit self-disposal to
wait on the operation currently trying to dispose the flow.

## Experiments and remaining cost

The isolated experiments compare current main with scope reduction (A), operation
ownership/root-state-machine consolidation (B), B plus explicit forwarding-task
identity tracking (C), and annotation-only forwarding (D). C demonstrates useful
explicit propagation inside the pipeline, but does not claim ambient-free public
disposal. Each implementation is measured separately against the same baseline;
B versus C isolates the additional forwarding optimization. A combined candidate
also establishes ownership at opaque execution boundaries.

Use `build/LifetimeProbe` for exact scope allocations and screening across
0/1/2/4/8 middleware layers, sequential/yielding delegates, bursts, 4/16 producers,
bounded admission in both capacity modes, bounded producers, keyed operations and
keyed producers. Use the unchanged 16-case BenchmarkDotNet suite for calibrated
before/after confirmation. Raw samples, patches, binaries' hashes and environment
records accompany the investigation; generated results are not source-controlled.
The comparison workflow runs full matched cases when lifetime sources change.

Some overhead remains necessary under the existing contract: an ambient frame and
execution-context change at a new logical owner; context capture/restoration across
async boundaries; independent activity tracking for detached/abandoned child work;
completion outcome/error processing; and admission/keyed reservations, callbacks,
cancellation sources, and caller-context capture. Existing per-registration public
context objects and continuation delegates also remain. Removing or pooling these
is a separate tradeoff and is not justified by scope-allocation measurements alone.

The recommendation is to keep ambient state for public reentrant-disposal detection
and use explicit operation state for accounting. Full `AsyncLocal` removal is not
compatible with unchanged arbitrary asynchronous caller behavior and disposal APIs.
