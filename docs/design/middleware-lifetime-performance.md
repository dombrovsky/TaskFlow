> Follow-up: [Further lifetime overhead investigation](middleware-lifetime-alternatives.md)
> evaluates ambient-free semantics and two additional allocation optimizations.
> The historical comparisons below describe the initial PR #54 implementation;
> subsequent cached root frames save 32 bytes per ordinary root entry, and root
> execution consolidation removes one 136-byte box when that wrapper suspends.

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
allocates nothing. A construction-only task probe measures 72 bytes without
ambient state and 152 bytes with ambient state, confirming the additional
80-byte task context storage. The original scope also allocates a 32-byte frame, 24-byte
closure, 64-byte delegate and 32-byte action disposable. Root entry adds a 72-byte
execution context/map; restoring a nonempty parent adds another 72 bytes. These
sizes are runtime-specific. The probe records raw observations separately from
whole-operation measurements.

The allocation traces for the healthy parent and regression commit identify the
following exact steady-state breakdown for the eight-layer synchronous forwarding
case (the trace's allocation weights are sampled; the object sizes, source-level
counts, exact allocation deltas and independent scope probes support this accounting):

| Added allocation in PR #34 | Bytes per operation |
| --- | ---: |
| Nine enqueue activity state-machine boxes, 128 bytes each | 1,152 |
| Outer `EnqueueCore` + `EnqueueWithCompletion` boxes: 128 + 160, replacing 152 | 136 |
| Submission root scope plus nested terminal scope: 224 + 296 | 520 |
| Two 80-byte task `ContingentProperties` objects for nondefault context capture | 160 |
| Bound `Lifetime.Exit` action | 64 |
| Growth of the existing operation from 80 to 120 bytes | 40 |
| **Total: 4,936 → 7,008** | **2,072** |

The two task context-storage objects appear because submission now sets ambient
state before `Task.Factory.StartNew` and `ContinueWith` capture their context.
Capturing a context is not synonymous with cloning it, but storing a nondefault
context on each task needs this extra storage. This contribution depends on the
caller: it may already be paid when user ambient state is present. Direct bare
flow submissions establish ownership inside the scheduled delegate instead, so
their observed regression is the root scope's 224 bytes alone.

Current main also includes PR #46's cancellation-context changes: the eight-layer
case adds another 200 bytes (17 operation contexts grow by eight bytes, and eight
completion closures grow by eight). That explains 7,008 at the regression commit
versus 7,208 at the optimization baseline; it is a separate change.

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
submission tasks. For eight layers against current main, the allocation reductions reconcile as
follows: A saves 400 bytes (416 bytes of scope objects/writes, offset by a 16-byte
larger suspended root state); B saves 304 (64-byte exit delegate, redundant
128-byte root activity box, and 112 bytes of outer-state consolidation); C saves
another 832 (seven 128-byte boxes plus eight bytes at the terminal tracker, offset
by nine continuation closures growing by eight bytes). Together this predicts
7,208 → 5,672 bytes, matching the full-workload measurements. The final candidate
uses one 192-byte root state-machine box and one 120-byte terminal enqueue tracker
in this forwarding case, instead of the previous root pair and nine wrappers.

The execution activity wrappers remain: arbitrary execution
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

## Measured before/after results

The [same-run GitHub comparison](https://github.com/dombrovsky/TaskFlow/actions/runs/39145624738)
compares current main `ccd52f2` with production implementation `5f5eb13`. It uses
SDK 10.0.100, runtime 10.0.0, BenchmarkDotNet 0.15.8, processor count two, three
warmups, eight actual iterations, one launch, identical compatible harness sources,
and baseline-first independent worktrees on one runner. The complete 16-case
[raw artifact](https://github.com/dombrovsky/TaskFlow/actions/runs/39145624738/artifacts/12143048721)
contains JSON measurements, variability, allocations, settings and source provenance.
The following rounded values are copied from that job's BenchmarkDotNet tables:

| Middleware layers | Before ns/op | After ns/op | Time change | Before → after bytes/op |
| --- | ---: | ---: | ---: | ---: |
| 0, bare flow | 1,227 | 1,110 | −9.5% | 736 → 616 |
| 1 | 3,167 | 2,642 | −16.6% | 2,784 → 2,087 |
| 2 | 3,646 | 2,984 | −18.2% | 3,416 → 2,600 |
| 4 | 4,540 | 3,710 | −18.3% | 4,679 → 3,624 |
| 8 | 6,235 | 5,556 | −10.9% | 7,208 → 5,672 |

The eight-layer allocation reduction is 21.3%. Burst means were 641.8 → 602.4 ns/op
with 915 → 803 bytes/op. Burst uncertainty overlaps, so this run does not establish
a precise burst speedup. Producer means for 1/4/16 producers were 612.2/710.3/714.5 →
542.2/693.9/706.1 ns/op; the smaller concurrent-producer differences are inconclusive.
Sequential synchronous/no-token work measured 1,102 → 1,012 ns/op, 736 → 616 bytes;
yielding/no-token work measured 1,824 → 1,717.2 ns/op, 944 → 832 bytes. Live and
cooperatively canceled caller tokens also retained lower allocations. Enqueue-only
timing has broad uncertainty and no allocation claim. These are one-run observations,
not universal thresholds or a complete recovery of the original regression.

A correctly paired local 16-case run on SDK 10.0.402/runtime 10.0.13 also reduced
eight-layer allocation from approximately 7,208 to 5,672 bytes and mean time from
6,250.6 to 4,149.6 ns/op. Its greater variability and different environment make the
GitHub result the preferred timing evidence. An earlier local long-duration pass
used the candidate project directory for both sides because BenchmarkDotNet resolves
its generated project from the working directory. It is retained as a same-source
control, excluded from before/after claims. Short-iteration exploratory runs and
screening probes are also kept separately in the raw investigation archive.

The [build workflow](https://github.com/dombrovsky/TaskFlow/actions/runs/39145624677)
passed the complete solution tests and required formatting checks on both runtimes.
The final focused local suite passed 122 tests on each runtime. Local full-suite
runs hit older cancellation/timing assertions; unchanged main reproduced those
failure families, and a 100 ms disposal assertion failed once only in the local
candidate run. It is not a reliable timing guarantee under container scheduling.
The successful CI full-suite run is separate evidence; no assertion was relaxed.
Library builds cover netstandard2.0/net8.0/net10.0, and trimmed plus NativeAOT smoke
applications executed lifetime, bounded, keyed and annotation-only work on both
.NET 8 and .NET 10. These are smoke checks, not exhaustive AOT application coverage.

### Subsequent CI disposal-race finding

A [later full-suite run](https://github.com/dombrovsky/TaskFlow/actions/runs/39146921191)
on the documentation/probe-only update failed `DisposeAsync_ShouldWaitQueuedOperations`
on both runtimes: the third returned task was still `WaitingForActivation` after
`DisposeAsync` completed. Production sources are identical to the earlier passing
implementation. This is stronger evidence than assuming every failure is a delay
assertion, so it was investigated separately.

A public-API probe ran 300 rounds of three dedicated-thread delegates, awaited
flow disposal, inspected returned tasks and delegate-completion counters, and then
awaited every task. Unchanged main had incomplete caller tasks in 104 rounds; the
candidate had 61. Both had zero unfinished delegates after disposal. These counts
show reproducibility, not a statistical comparison of race probability.

`ThreadTaskFlow` signals its thread-completion promise after underlying execution
items finish. Its `ExecutionItem.GetTypedTask<T>` and outer `Enqueue<T>` async
wrappers can still have queued continuations publishing caller-facing completion.
That explains the observed race between disposal and returned-task completion.
The optimization preserves underlying-work quiescence; it does not repair this
preexisting caller-task publication race. The probe source and outputs, both CI
logs, and this limitation are retained with the investigation. The PR remains a
draft, with no relaxed test assertions. A separate thread-flow completion/drain
change should address the existing race with deterministic coverage.

## Experiments and remaining cost

The isolated experiments compare current main with scope reduction (A), operation
ownership/root-state-machine consolidation (B), B plus explicit forwarding-task
identity tracking (C), and annotation-only forwarding (D). C demonstrates useful
explicit propagation inside the pipeline, but does not claim ambient-free public
disposal. Each implementation is measured separately against the same baseline;
B versus C isolates the additional forwarding optimization. A combined candidate
also establishes ownership at opaque execution boundaries. A throwaway variant
retaining explicit accounting while making ambient entry a no-op fails the nested
owner guard test after `await`; its rejected patch and test output are retained.
It is excluded from performance claims.

Calibrated independent local middleware runs isolate the variants as follows:

| Variant | Eight-layer mean ± SD, ns/op | Managed bytes/op |
| --- | ---: | ---: |
| Current main | 6,250.6 ± 731.0 | 7,207 |
| A: scopes only | 6,856.5 ± 623.4 | 6,806 |
| B: ownership/root consolidation | 6,310.4 ± 740.1 | 6,902 |
| C: B + forwarding identity | 4,539.5 ± 356.1 | 6,072 |
| Combined A+B+C | 4,149.6 ± 425.9 | 5,671 |

These runs use the same SDK/runtime and settings as the local full comparison.
They are ordered runs with substantial timing variability; the scope-only and
root-only timing differences do not establish a separate speedup. The allocation
changes and state-machine sizes provide the stronger attribution. The combined
change is independently confirmed by the less variable GitHub comparison.

D's matched annotation-only benchmark measured 2,409.7 ± 427.9 → 1,536.6 ± 42.2
ns/op and 2,151 → 956 bytes/op against current main, with the original scope code
on both sides. The combined candidate also applies A's scope reduction.

Coordination screening medians (whole-process managed bytes per operation,
including harness overhead) were 3,112 → 2,421 for outstanding admission,
3,271 → 2,583 for pending admission, 3,247 → 2,592 for bounded producers,
4,387 → 3,683 for keyed scheduling, and 4,115 → 3,384 for keyed producers.
Those short timing samples vary substantially, including changing directions
for concurrent keyed work; they support allocation screening, not firm latency
or throughput claims. All raw per-iteration samples are retained.

Use `build/LifetimeProbe` for exact scope allocations and screening across
0/1/2/4/8 middleware layers, sequential/yielding delegates, bursts, 4/16 producers,
bounded admission in both capacity modes, bounded producers, keyed operations and
keyed producers. Use the unchanged 16-case BenchmarkDotNet suite for calibrated
before/after confirmation. Raw samples, patches, binaries' hashes and environment
records accompany the investigation; generated results are not source-controlled.
The comparison workflow runs full matched cases when lifetime sources change.

A yielding trace also identifies 136-byte `InvokeExecution` state-machine boxes.
A fully pending eight-layer execution can need nine such boxes (1,224 bytes), in
addition to the envelope, delegate and terminal async states. A `Task.Yield()`
workload can finish while outer wrappers are still unwinding, so its actual box
count is scheduling-dependent. These retained execution wrappers protect abandoned
or repeated asynchronous execution continuations; synchronous allocation figures
do not capture this cost.

For the synchronous eight-layer case, the remaining lifetime-related allocation
over the healthy parent reconciles to 536 bytes: a 104-byte scope, 160 bytes of task
context storage, 40 bytes of operation growth, 40 bytes of root state growth, a
120-byte terminal tracker, and 72 bytes of larger enqueue closures. Current main's
separate cancellation-context changes contribute the other 200 bytes above that
parent. This identifies opportunities separately from claiming every byte is
intrinsically unavoidable; removing the guard or independent-work tracking is not
a correct optimization.

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
