> Current contract: [External-owner disposal](middleware-external-owner-disposal.md).
> On 2026-10-19, operation self-disposal was explicitly designated unsupported.
> Per-operation ambient tracking has been removed; recommendations and measurements
> below describe the earlier investigation and its then-current contract.

# Further lifetime overhead investigation

Baseline: draft PR #54 at `c50eed3000916b83342e285421df203f5895bd84`,
not the unoptimized PR #34 merge or release 1.0.0. Each variant is isolated.
All figures below are local .NET 10.0.13, SDK 10.0.402, Linux x64, with
DOTNET_PROCESSOR_COUNT=2. No builds/tests ran during timed benchmark execution.

## What ambient state actually buys

The sole reader of TaskSchedulerMiddlewareLifetime.Current is the lifetime's
EnterContext/CheckReentrantDisposal logic. No admission, queue ordering,
completion outcome, cleanup ownership, or work-draining decision reads it.
PipelineOperation carries the owner and tracks independent work explicitly.

The ambient chain rejects a disposal call made from that lifetime's operation,
registration factory, or cleanup, including after await and through nested
owners. Rejection happens before shutdown changes the owner's state. It is
useful protection against a self-wait: an operation awaiting DisposeAsync waits
for disposal, which waits for that same operation to finish. Factories and
cleanup can create the same recursive wait. This guard is a defensive diagnostic
contract; normal externally owned shutdown does not need logical caller identity.

It is not a general deadlock detector. Awaiting another operation on the same
sequential lane can still deadlock. Descendants inherit the guard even after the
originating operation has completed; current regression tests explicitly require
that behavior. Suppressing context flow before creating a child can remove its
ancestry. Replacing the chain with an active-operation flag would change these
semantics, not merely optimize their implementation.

Explicit ownership can replace ambient state for lifetime accounting, but cannot
identify an arbitrary caller of parameterless DisposeAsync after await. Thread
identity and Task.CurrentId do not supply logical async ancestry. Existing public
operation contexts are not required arguments to user delegates or disposal.

## Is a design without AsyncLocal reasonable?

Yes, under an explicit external-owner shutdown rule. Keep counting outstanding
work, close admission, request cancellation, drain work, and only then clean up.
Document that work/factories/cleanup must not await disposal of their own owner.
This preserves normal queue and cleanup correctness; accidental self-disposal
loses the current InvalidOperationException and may hang instead.

A separate nonblocking RequestStop API could make shutdown requests from inside
work safe and intentional, with DisposeAsync/Completion awaited by the external
owner. That is an API design proposal, not implemented here. Cancellation,
closing entry, initialization and races must follow the existing shutdown state
machine; fire-and-forget disposal is not a substitute for a designed stop API.

For compatibility, a per-operation reentrancy-diagnostics option could default to
today's behavior, letting an application explicitly choose the cheaper external
ownership contract. Cold factory/cleanup scopes can retain their guard without
setting AsyncLocal for each normal enqueue. Neither an opt-out nor a new stop API
has been shipped by this investigation. Default guarantees in PR #54 remain intact.

## Variants

- `root-node`: initial prototype represented an empty-parent root directly by the
  lifetime in AsyncLocal<object?>; distinct-owner nesting still used immutable
  frames. Both runtimes passed all 122 focused tests. This was superseded in the
  same worktree by **cached-root**: reuse one immutable root Frame per lifetime.
  Three small edits retain the original strongly typed ambient representation.
  Only empty-parent entries use the cached frame; nesting under another owner
  still allocates a frame. Concurrent contexts safely share immutable frames.
  Both runtimes passed all 122 focused tests with this cached variant as well.
  Costs one 32-byte frame and one reference field per lifetime, saving 32 bytes
  on each subsequent ordinary root entry. No operation/delegate is retained.
- `execution-tracking`: move the root execution activity into the existing async
  ExecuteEnvelope method; it invokes InvokeExecutionCore directly. Child execution
  continuations retain independent activity tracking. This removes one potentially
  suspended InvokeExecution state machine and keeps completion middleware within
  the envelope's activity. Both runtimes passed all 122 focused tests.
- `no-ambient`: remove scopes from submissions, built-in terminal execution,
  registration, and cleanup; make the reentrant guard a no-op. Keep all lifetime
  Enter/Exit and operation AddActivity/ReleaseActivity accounting. This is an
  experimental semantic control and intentionally does not preserve guard tests.
  The selected 84 tests passed on both runtimes, covering bounded admission,
  keyed scheduling, execution cancellation/replacement, and discarded work
  retaining capacity until quiescence. This is not a full compatibility claim.

`no-ambient-self-disposal.jsonl` demonstrates the tradeoff without hanging a test:
work suspends, initiates disposal, publishes that shutdown task, and waits on a
separate gate. Shutdown is incomplete while the work remains active. The external
caller opens the gate, then both the operation and shutdown complete successfully.
Awaiting the shutdown task inside that operation instead would create a cycle.

## Measurement limits

Screening results cover 0/1/2/4/8 middleware, sequential sync/async work, bursts,
concurrent producers, both bounded capacity modes, bounded producers, and keyed
scheduling/producers. They include harness overhead and showed substantial timing
and tiering variation; do not treat screening timings as calibrated speedups.

The separate BenchmarkDotNet runs use the unchanged synchronous forwarding
middleware, bare sequential, and drained-burst cases, plus the same new yielding
middleware benchmark in all measured worktrees. Each invocation runs from its own
worktree so generated benchmark projects reference the intended library. Raw
samples, generated reports, logs and variant patches accompany this report.

Task context-storage savings in an otherwise empty ExecutionContext are not an
application-wide guarantee. An application's own AsyncLocal values (for example
tracing state) can already require context capture/storage even if TaskFlow stops
adding its own value. AsyncLocal removal does not mean ExecutionContext flow can
be suppressed: caller context semantics must remain intact.

No pooling, ThreadStatic replacement, custom async builders, public API changes,
or default disposal-guarantee changes have been introduced.

## Calibrated comparison

Each timing cell is mean ± BenchmarkDotNet's 99.9% confidence-interval margin,
in nanoseconds/operation. Allocation is bytes/operation. Timing intervals overlap
widely: these runs **do not establish a reliable speedup** for either variant.
This includes rows whose point estimate looks faster or slower. Allocation changes
are much more stable. Yielding pipelines can vary in how many wrappers actually
suspend before their child completes; burst allocation also depends on overlap.

| Case | PR #54 ns ± margin; B | No ambient ns ± margin; B | Root execution consolidation ns ± margin; B |
|---|---:|---:|---:|
| SubmitAndDrain | 484 ± 156; 756 | 387 ± 57; 606 | 481 ± 81; 738 |
| PipelineCompletion(Layers: 0) | 1193 ± 160; 616 | 1142 ± 133; 512 | 1338 ± 183; 616 |
| PipelineCompletion(Layers: 1) | 2449 ± 662; 2083 | 2418 ± 610; 1798 | 2595 ± 638; 2083 |
| PipelineCompletion(Layers: 2) | 2814 ± 1154; 2595 | 2303 ± 275; 2309 | 3439 ± 3205; 2593 |
| PipelineCompletion(Layers: 4) | 3336 ± 1044; 3623 | 3602 ± 1558; 3335 | 3301 ± 1315; 3623 |
| PipelineCompletion(Layers: 8) | 4394 ± 1492; 5671 | 4760 ± 2069; 5384 | 4548 ± 1333; 5671 |
| AsyncCompletion(Token: None) | 1526 ± 253; 832 | 1722 ± 201; 712 | 1860 ± 922; 832 |
| SyncCompletion(Token: None) | 1493 ± 457; 616 | 1143 ± 115; 512 | 1375 ± 577; 616 |
| PipelineYield(Layers: 1) | 3479 ± 1209; 2767 | 3050 ± 328; 2440 | 3552 ± 1072; 2631 |
| PipelineYield(Layers: 8) | 5986 ± 1382; 7293 | 6194 ± 2797; 6974 | 7903 ± 4790; 7164 |

With an otherwise empty context, removing ambient tracking reduced bare sync
allocation 616 → 512 B (−16.9%), bare yielding allocation 832 → 712 B (−14.4%),
and eight-layer sync allocation 5,671 → 5,384 B (−5.1%). The latter is about 288 B,
not the entire remaining pipeline allocation regression. It does not eliminate
per-registration context objects/delegates, activity synchronization, or suspended
execution wrappers. The exact earlier allocation trace measured the synchronous
baseline at 5,672 B; the calibrated mean here differs by normal task-completion
racing/caching in the benchmark.

## Recommendation

Keep the default rejection contract for existing APIs. The 1.0 release comparison
and PR #54 results remain the appropriate CI evidence for the larger regression;
local noisy point estimates must not replace that evidence. Retain PR #54's
wrapper consolidation and explicit operation accounting. A cached immutable root
frame and one less root execution wrapper are simple candidates for further
allocation reduction without removing the guard. They were subsequently combined in PR #54 after targeted validation. Their
allocation savings are established; no material timing recovery is claimed from
these local measurements.

If a low-allocation mode is wanted, make per-operation reentrant-disposal detection
an explicit choice and document the external-owner rule. The diagnostic is useful,
but ordinary FIFO scheduling and safe draining do not inherently require it.
Do not silently remove it for all callers or replace AsyncLocal with thread/global
identity. A nonblocking stop request plus external drain is a reasonable future
API for applications that intentionally initiate shutdown inside work. Full ambient
removal is feasible with that different misuse contract; full removal preserving
the current arbitrary after-await rejection behavior is not demonstrated.

## Focused confirmation and implementation

A custom inline terminal returns the asynchronous envelope before a gate is opened.
The probe completes the gate only after Enqueue has returned, forcing every
execution wrapper to suspend. Continuations finish inline, which the probe checks,
so the current-thread allocation counter includes the full operation. With the
same terminal, workload, gate and middleware in all variants:

| Forwarding layers | PR #54 initial | Root execution consolidation | Ambient-free control |
|---|---:|---:|---:|
| 1 | 1,952 B | 1,816 B | 1,800 B |
| 8 | 6,488 B | 6,352 B | 6,336 B |

Consolidation saves exactly 136 B at either depth: one suspended async state
machine. This is a focused allocation result, not an end-to-end timing estimate.
The inline terminal avoids the built-in terminal's Task context-storage costs,
which explains why ambient removal saves fewer bytes here than in that benchmark.

The cached-root scope probe measured 104 → 72 B/root entry and zero bytes for
same-owner nesting. The initial direct-root object representation is retained only
as an archived experiment; production retains AsyncLocal<Frame?> and caches an
immutable Frame per owner. PR #54 receives only cached-root and root execution
consolidation. No ambient-free or diagnostics opt-out code is included.

The combined production implementation measured 1,784 B (one layer) and 6,320 B
(eight layers) in the same forced-pending probe: 168 B less than the initial
implementation at either depth. Its root scope measured 72 B and same-owner nesting
zero B. The combined focused suite passes 122 tests on each of .NET 8 and .NET 10;
netstandard2.0, .NET 8 and .NET 10 builds and repository formatting checks pass.
The 10-case calibrated timing table above measures the independent variants,
not the subsequently combined implementation. Fresh full CI remains pending.
