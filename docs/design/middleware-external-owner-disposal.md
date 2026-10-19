# External-owner disposal and operation lifetime tracking

Flows and explicit middleware lifetimes must be disposed by their external owner.
Calling Dispose, Dispose(TimeSpan), or DisposeAsync from an enqueue delegate or
middleware invocation running on that owner is unsupported. Such a call can wait
for itself; no InvalidOperationException or other self-disposal diagnostic is
promised. This contract was explicitly requested on 2026-10-19.

## Implementation

Remove lifetime scopes from all four per-operation sites: TaskFlow.RunAfterPrevious,
ThreadTaskFlow's delegate wrapper, PipelineTaskScheduler.EnqueueCore, and
PipelineTaskScheduler.ExecuteEnvelope. No submission or execution path reads or
writes the lifetime AsyncLocal. Remove the cached root frame and its per-lifetime
reference field, because operations no longer need frames.

The only remaining ambient scopes are EnterOwnerCallback in registration factories
and owned cleanup. They retain nested registration/cleanup reentrancy rejection,
including after await. They impose no per-enqueue tracking cost. Disposal itself
checks this callback context before changing state. Ordinary ExecutionContext flow
is preserved, including application AsyncLocal values, suppressed submission flow,
custom schedulers, and synchronization contexts.

Work ownership continues explicitly through PipelineOperation.Lifetime and the
existing activity counter. Lifetime Enter/Exit, operation AddActivity/ReleaseActivity,
completion ownership, admission reservations, keyed coordination, and cancellation
accounting are unchanged. Disposal still closes entry, cancels/stops work, drains
accepted and independent underlying work, and cleans owned resources in reverse
order. Capacity cannot be released just because a caller-facing timeout wins.

No public signature, configuration option, pooling, or custom async builder is
introduced. Disposal from operation code is excluded from the supported contract,
not replaced by a different runtime heuristic. ThreadStatic, Task.CurrentId, and
global activity flags are not used as substitutes.

## Correctness coverage

The focused suite passes 121 tests on each of .NET 8 and .NET 10. Previous operation
self-disposal assertions were removed or replaced with external-disposal tests;
factory and cleanup rejection tests remain. Coverage includes real suspension,
suppressed caller context, nested flows disposed externally, explicit lifetimes over
opaque schedulers, annotation-only custom flows, concurrent caller AsyncLocal
values, cancellation/replacement, bounded admission, keyed scheduling, and abandoned
continuations retaining capacity until underlying work finishes.

A child task may dispose the owner after its originating operation has settled.
Parameterized tests cover bare and middleware pipelines. The previous ambient
ancestry marker rejected that valid external shutdown indefinitely; the updated
code no longer carries that marker into child tasks.

The full solution run and its failures are retained in the raw artifacts; focused
success is not a claim that the full suite is green. See validation below.

## Benchmark method

The paired local baseline is the previous PR #54 head,
395eccb22ae0bcca84fc07f56a6efc7faa6d8a3c, which already includes cached root frames
and root execution consolidation. The candidate removes operation scopes under the
external-owner contract. Both use the unchanged TaskFlow.Benchmarks harness,
.NET 10.0.13 / SDK 10.0.402, Linux x64, DOTNET_PROCESSOR_COUNT=2, three warmups,
eight actual iterations, and one launch. Each run starts from its own worktree so
generated benchmark projects reference the intended library. Baseline runs first;
shared-runner noise and temporal drift remain limitations. No builds/tests/traces
run concurrently with timed measurements.

All 16 compatible cases are included: 0/1/2/4/8 synchronous forwarding middleware,
sequential sync/async with absent/live/canceled caller tokens, drained bursts,
1/4/16 concurrent producers, and enqueue-only work. Allocation is unavailable for
the enqueue-only case. Screening separately covers bounded pending/outstanding
admission, bounded producers, keyed scheduling and keyed producers, with eight raw
samples per case. Its timing results are exploratory rather than calibrated.

The --pending probe uses a custom inline terminal and completes its work gate only
after Enqueue returns. Every execution wrapper must therefore suspend. Continuations
are checked to finish inline, so current-thread allocation counters include the
entire operation. It measures allocation, not end-to-end throughput.

Applications with their own ambient values can already incur Task context-storage
costs. Removing TaskFlow's marker does not guarantee those existing costs vanish;
ExecutionContext flow is preserved rather than globally suppressed.

## Matched local measurements

Timing cells are mean ± BenchmarkDotNet's 99.9% confidence-interval margin,
in nanoseconds/operation. Timing uncertainty is wide and often overlapping;
these runs do not establish a reliable middleware timing improvement. Eight-layer
point timing increased while its allocation fell; neither a timing win nor a new
regression is established by that noisy pair. The two equivalent bare-flow cases
also have different point changes, illustrating the runner variation.

| Case | Before ns ± margin | After ns ± margin | Before → after B/op |
|---|---:|---:|---:|
| SubmitAndDrain | 441 ± 60 | 356 ± 41 | 720 → 626 |
| SubmitOnly | 503 ± 303 | 279 ± 123 | unsupported |
| PipelineCompletion(Layers: 0) | 1407 ± 279 | 1086 ± 118 | 584 → 512 |
| PipelineCompletion(Layers: 1) | 2566 ± 954 | 2207 ± 225 | 2055 → 1798 |
| PipelineCompletion(Layers: 2) | 2987 ± 937 | 2477 ± 236 | 2565 → 2311 |
| PipelineCompletion(Layers: 4) | 3317 ± 742 | 3178 ± 499 | 3591 → 3335 |
| PipelineCompletion(Layers: 8) | 5293 ± 890 | 5598 ± 1803 | 5640 → 5385 |
| ConcurrentSubmitAndDrain(Producers: 1) | 476 ± 52 | 373 ± 32 | 714 → 617 |
| ConcurrentSubmitAndDrain(Producers: 16) | 601 ± 157 | 547 ± 110 | 780 → 698 |
| ConcurrentSubmitAndDrain(Producers: 4) | 537 ± 77 | 523 ± 89 | 780 → 681 |
| AsyncCompletion(Token: CanceledCooperative) | 1537 ± 273 | 1662 ± 352 | 816 → 728 |
| AsyncCompletion(Token: Live) | 1472 ± 220 | 1658 ± 412 | 816 → 728 |
| AsyncCompletion(Token: None) | 1567 ± 306 | 1559 ± 168 | 800 → 712 |
| SyncCompletion(Token: CanceledCooperative) | 1298 ± 184 | 1157 ± 119 | 600 → 528 |
| SyncCompletion(Token: Live) | 1257 ± 312 | 1205 ± 142 | 600 → 528 |
| SyncCompletion(Token: None) | 1200 ± 87 | 1212 ± 239 | 584 → 512 |

The repeatable allocation changes are:

- Bare synchronous operations: 584 → 512 B (−12.3%).
- Bare yielding operations: 800 → 712 B (−11.0%).
- Eight forwarding layers: approximately 5,640 → 5,385 B (−4.5%).
- Bursts: 720 → 626 B in this run; burst overlap also influences allocation.

These are incremental changes from 395eccb, not the full improvement from main or
PR #34. The earlier investigation measured current-main eight-layer allocation at
7,208 B and release 1.0.0 at 4,936 B. The candidate's roughly 5,384 B is still about
9% above that release allocation. These historical figures are allocation context,
not a fresh paired timing comparison with the release. A fresh comparison workflow
runs on this PR against release 1.0.0 and merge-base.

## Focused allocation attribution

A non-tiered diagnostic run (DOTNET_TieredCompilation=0) makes object counts stable
through the forced-suspension probe. All other environment settings are identical.
It measures 1,720 → 1,600 B with one layer and 6,256 → 6,136 B with eight layers:
exactly 120 B less at either depth. These numbers include the gate and custom inline
terminal workload and must not be substituted for the built-in scheduler table.
The ordinary tiered probe results are retained separately; its eight-layer
allocation changed during warmup, so fractional samples are not used as exact
object-count evidence.

At a normal root entry, operation ambient tracking previously allocated 72 B for
ExecutionContext and its value map; the immutable root Frame was cached by the
previous commit. That allocation is gone. Smaller async state machines account for
additional allocation savings when they suspend. With the built-in terminal, an
otherwise default caller context also avoids Task context-capture storage that the
extra lifetime marker previously induced. Middleware allocation is reduced by
about 256 B per synchronous operation. Existing application ambient state can
still require context-capture storage, so that saving is workload dependent.

The owner-callback scope probe now measures cold factory/cleanup scopes only.
Removing the cached frame makes a cold root callback scope 104 B rather than 72 B,
while removing a 32-byte frame and reference field from each owner's construction.
This is a cold-path tradeoff; ordinary operations create no lifetime frames or
lifetime context maps. Same-owner nested callback scopes remain allocation-free.

Remaining overhead includes per-operation ownership/activity synchronization,
per-registration contexts/delegates, independent asynchronous continuations,
completion outcomes, and bounded/keyed coordination when configured. Those costs
protect supported work-draining and capacity behavior; removing the unsupported
self-disposal guard does not justify dropping that accounting.

## Validation and limits

- 121 focused tests pass on each of .NET 8 and .NET 10.
- netstandard2.0, .NET 8 and .NET 10 library builds pass with zero warnings/errors.
- Repository whitespace and file-scoped namespace checks pass. The formatter
  reports existing workspace-loading warnings but exits successfully.
- The unchanged benchmark validator passes all 16 cases, each invoked twice with
  1,000 checked results per batch.
- Trimmed NativeAOT smoke applications publish and run on .NET 8 and .NET 10,
  exercising middleware, bounded admission, keyed scheduling and annotation-only
  submission under external disposal. These are smoke checks, not exhaustive AOT
  application coverage.

The candidate full core suite fails 7/400 tests on .NET 8 and 10/400 on .NET 10.
The unchanged 395eccb control fails 5/401 and 7/401 respectively. Failure counts are
observations, not failure probabilities. Both reproduce cancellation-scope timing,
shared-producer cleanup timing, and the 100 ms synchronous-disposal assertion;
individual cases/runtimes vary. The candidate also hits the previously reproduced
thread-flow caller-task completion race. That same assertion failed in the
[prior commit's CI](https://github.com/dombrovsky/TaskFlow/actions/runs/39149899677),
before this contract change. Historical probes reproduce it on unchanged main.
Dependency-injection, logging and time-extension suites pass for the candidate on
both runtimes. No timing assertion or work-quiescence guarantee has been relaxed.
The full suite is not green; the PR remains a draft with these failures visible.

Raw artifacts retain both complete BenchmarkDotNet result trees, all screening
samples, controlled and tiered allocation probes, source/binary provenance,
benchmark validation, both full-suite logs, targeted tests, formatting logs and
.NET 8/.NET 10 NativeAOT build/run logs.
