# Dispatch ownership and keyed throughput review

Reviewed `7fa12ae` against its parent `03273aa`. Production scheduling/context code is unchanged by this review. The changes are fault tests, measurement tools/data, and documentation.

## Ownership and exceptions

`_pumping` covers both a queued worker and an active drain. The scheduling lock protects the idle-to-owned transition, ready queues, and publication of idle. An inline legacy drain transfers ownership to a queued worker without first publishing idle. An arrival during retirement either joins the owner or observes idle and schedules the next worker. No worker waits for execution capacity: the coordinator retires when nothing is runnable; completion/cancellation/start transitions recheck it.

The runtime-source audit examined the [NET 8 overload](https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/System.Private.CoreLib/src/System/Threading/ThreadPoolWorkQueue.cs#L1575), [NET 10 overload and enqueue](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Threading/ThreadPoolWorkQueue.cs#L1901), and [Framework reference implementation](https://github.com/microsoft/referencesource/blob/main/mscorlib/system/threading/threadpool.cs#L1621) relevant to netstandard2.0 consumers. These implementations return true after enqueueing; there is no bounded-queue rejection/false-return branch. The callback is a fixed, non-null static delegate. Saturation delays processing; it does not reject it.

**Failure is nevertheless possible.** Allocation can fail before enqueue; requesting a worker can fail after the callback is already published. NET 10's `Enqueue` adds the item before `EnsureThreadRequested`, and [worker creation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Threading/PortableThreadPool.WorkerThread.cs#L270) can allocate/start a thread. Resource exhaustion, unsupported hosting, runtime termination, and forced thread abort are outside the scheduler's progress guarantee. This does not assert that every resource exception always terminates the process.

| Failure | Effect and guarantee |
| --- | --- |
| Delegate, execution/completion middleware, or custom terminal `TaskScheduler.QueueTask` throws | Existing submission `ExecuteAsync` catches scheduling/execution failures; the pipeline converts completion middleware failures into outcomes. The accepted completion faults/cancels and quiescence releases reservations. The drain continues. |
| Runtime cannot publish/start a worker | `_pumping` can theoretically remain owned without a progressing worker. Some callers may fault while other admitted work remains pending. Neither all completions nor shutdown are unconditionally guaranteed under this resource/hosting failure. |
| Exception escapes the raw worker callback | An ordinary unhandled thread-pool callback exception normally terminates the process. Internal dispatch only calls an async entry point with its own failure conversion; there is no supported user callback outside that boundary. Null/invalid captured contexts are excluded by construction/lifetime. |
| AsyncLocal notification throws while restoring a context | NET 8/10 `ExecutionContext.OnValuesChanged` calls [FailFast](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Threading/ExecutionContext.cs#L435). A drain catch cannot recover this. |
| Throwing/unstable key comparer or noncooperative blocking backend/delegate | Outside the documented comparer/progress contract. No forced interruption, durable delivery, or shutdown-time bound is promised. |

No defensive reset/retry was added. An enqueue exception does not reliably identify whether the runtime already published the callback. Resetting ownership can create competing drains; retrying uses the same failed resource and may schedule duplicates. An inline fallback violates the producer guarantee. Closing/settling a domain still needs memory, continuations, and functioning completion middleware. Recovering only this queue call would also leave other allocating admission/bookkeeping paths uncovered. There is no independently recoverable thread-pool rejection for a fault seam to model faithfully.

Under normal cancellation/shutdown, keyed `Close` settles admission waiters and admitted undispatched work; dispatched work remains owned until quiescence. Uncoordinated bounded admission closes waiters and relies on dispatch/terminal cancellation to settle already admitted work. Thus resource failure cannot be claimed to be repaired by cancellation/disposal in every configuration. Supported cancellation/disposal races remain covered by the existing deterministic tests.

`SynchronousSchedulingAndCallbackFailures_DoNotAbandonTheDrain` adds three deterministic cases (bounded, keyed, concurrent). It holds the first custom `QueueTask`, fills the existing queue, then throws from that scheduler call, another delegate, and completion middleware. It checks observable failures, successful later work on the same drain, nonexecution of the scheduling-rejected delegate, reuse of failed-key/global capacity, and completed disposal. This tests recoverable failures at the actual user-controlled boundary without inventing a runtime failure contract.

## Repeated measurements

Same Linux x64 environment, SDK 10.0.402/runtime 10.0.13, two-CPU quota, workstation GC. Both assemblies use the same extended harness, identical short `ValueTask<int>` operations, distinct integer keys, default scheduler, one producer, and capacity/concurrency equal to operation count. Baseline instrumentation only counts its original `Task.Run(Pump)` calls. There were no concurrent builds/tests during measurement. Original results are retained in the original CSV files.

The added filters run only this scenario, so processes do not inherit the original full matrix's prior thread-pool/JIT state. This is an intentional control, not an exact recreation of that history. The short series uses 1,000 warmup operations, ten 20,000-operation samples per process, and old/head/head/old/head/old/old/head order (40 samples per variant). The warm series uses three 200,000-operation warmups, twelve 200,000-operation samples, old/head/head/old order (24 per variant). Both use the original 32-worker minimum. A sensitivity series uses two 200,000-operation warmups, ten samples, the same four-process order, and a two-worker minimum (20 per variant). A minimum is not a cap; actual worker counts are recorded.

Throughput is end-to-end, including draining/disposal. Allocation is process-wide, as in the original harness. CPU, contention, completed-work-item, thread, and GC counters are diagnostic totals, including framework/harness work. They do not isolate coordinator costs. Full GC occurs before each measured sample; JIT/PGO, GC generations, pool adaptation, and external scheduling still vary. These are samples from a constrained shared host, not statistically independent controlled-machine experiments.

### Distributions

Operations/s; quartiles use inclusive interpolation. Raw observations: [review-throughput.csv](review-throughput.csv). Process commands, order, durations, and available CPU-quota deltas: [review-processes.json](review-processes.json).

| Series / version | Samples | Minimum | P25 | Median | P75 | Maximum |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Short / 03273aa | 40 | 52,114 | 68,902 | 82,181 | 103,557 | 155,988 |
| Short / 7fa12ae | 40 | 81,315 | 107,457 | 119,110 | 140,566 | 193,302 |
| Warm / 03273aa | 24 | 75,073 | 83,241 | 93,150 | 113,086 | 192,848 |
| Warm / 7fa12ae | 24 | 72,899 | 86,836 | 92,340 | 114,036 | 183,326 |
| Two-worker minimum / 03273aa | 20 | 77,444 | 84,708 | 91,899 | 126,218 | 199,459 |
| Two-worker minimum / 7fa12ae | 20 | 81,800 | 91,256 | 97,390 | 111,228 | 152,462 |

The original −15.4% median difference did not reproduce as a stable effect: the short-series median difference was +44.9%, warm −0.9%, and worker-minimum sensitivity +6.0%. The warm interquartile ranges overlap almost completely. Process medians also vary substantially: baseline warm processes were 100,376 and 88,266 operations/s; head processes were 94,657 and 91,897. With the two-worker minimum, baseline process medians were 132,722 and 85,570, while head medians were 96,281 and 106,754. Comparing a single fast process against a slower one can easily produce the original percentage or reverse its sign.

Warm-series medians per 200,000 operations:

| Metric | 03273aa | 7fa12ae |
| --- | ---: | ---: |
| Bytes/operation | 4,886.1 | 4,230.9 |
| Scheduled drains | 200,000 | 199 (range 1–3,907) |
| Completed pool work items | 600,452.5 | 400,719 |
| Process CPU milliseconds | 3,447.4 | 3,126.8 |
| Contended lock acquisitions, process-wide | 1,705.5 | 1,411 |
| Ending worker count | 51 | 52 |

The short-series drain medians were 20,000 versus 263.5 (head range 2–6,521). A drain is scheduled only for an idle-to-runnable transition; fewer operations per drain in one run is not lost work. All accepted completions/results were checked. The roughly one-work-item-per-operation reduction is also visible in the completed pool counts. Remaining approximately two items per operation come from terminal scheduling and asynchronous completion/continuation work, with additional harness/runtime activity.

## Profiling and coordinator locks

Separate EventPipe traces used dotnet-trace 10.0.750501, `dotnet-sampled-thread-time`, and runtime contention/GC/threading events, with two 200,000-operation warmups and four samples per version (1.2 million operations including warmups). Profiling results are not mixed into the throughput distributions. This is sampled **thread wall time**, including waits, rather than a kernel CPU profile. In the inclusive stack report, `Monitor.Enter_Slowpath` accounted for 13.61% before and 10.63% after; `ExecutionContext.RunInternal` was 3.40% and 4.14%. Those percentages cannot isolate CPU overhead or establish a context-restoration regression.

Contention starts were classified by the nearest coordinator/lifetime method in their stack, then paired with stop events on thread ID. [review-contention.json](review-contention.json) retains counts and aggregate wait durations. Baseline `Pump` accumulated 33,319 ms of contended waiting; head `Drain` + `Pump` + `RequestDispatch` accumulated 2,119 ms. Per-operation `Started`/`Complete` paths remain significant (11,949 ms before, 10,546 ms after). These sums span concurrently waiting threads and can exceed elapsed wall time; trace overhead and different scheduling prevent treating them as production latency estimates.

A separate intrusive timing run used `instrument-locks.py` in disposable copies of both revisions. It replaces coordinator locks with stack-only timestamp scopes, recording acquisition time and time inside the original critical-section body. It adds a metric lookup inside the lock; timing excludes that lookup and the counter update but necessarily perturbs scheduling. Time held includes preemption/GC pauses, not just executed CPU instructions. Two warmups plus four measured 200,000-operation samples per process ran in old/head/head/old order, giving eight samples per version. These binaries were not used for the uninstrumented throughput claims.

Median summed critical-section body time per sample was 535.5 ms before and 425.0 ms after; summed acquisition time was 24,325 ms and 12,804 ms. The largest held-time category remained `Schedule` (299.5 → 243.3 ms, 400,000 acquisitions in each revision). `Started` held 30.5 → 32.8 ms; `Complete` 66.4 → 43.1 ms. Dispatch bookkeeping held 122.8 ms in old `Pump`; the head medians were 59.1 ms in `Drain`, 12.1 ms in `Pump`, and 6.1 ms in `RequestDispatch`. Category medians do not sum to the median of totals. Per-section observations: [review-locks.csv](review-locks.csv).

Source review agrees with these observations: key lookup/allocation remains under `Schedule`'s existing lock; admission and fairness use the existing dictionaries/ready list. The producer's new dispatch request only probes its ready head and execution policies. There are no added key lookups or fairness scans per producer. Empty-waiter scans avoid the old temporary HashSet allocation. Both revisions already capture keyed operation contexts; the head restores each context on a shared context-free drain and avoids redundant lifetime frames. No new long critical section or measured context bottleneck justifies changing that behavior.

The evidence supports fewer work items, lower allocation, and less dispatch contention. It does **not** support assigning the original −15.4% to a reproducible code-path regression. Adaptive pool state, GC/JIT history, process order, and short shared-host samples are the likely explanation for that estimate; their individual causal contributions were not isolated. Coalescing changes work placement and pool feedback, so a universal throughput improvement is not promised. No optimization or scheduler redesign was applied.

## Reproduce the focused comparison

Build the same harness against the instrumented baseline and the head using the main README commands. Run the short series in old/head/head/old/head/old/old/head process order; run the warm and sensitivity series in old/head/head/old order. Each invocation emits one process's samples:

```sh
dotnet /tmp/dispatch-old/Dispatch.dll 20000 10 short-default true 1 1000 1 32
dotnet /tmp/dispatch-new/Dispatch.dll 20000 10 short-default true 1 1000 1 32
dotnet /tmp/dispatch-old/Dispatch.dll 200000 12 short-default true 1 200000 3 32
dotnet /tmp/dispatch-new/Dispatch.dll 200000 12 short-default true 1 200000 3 32
dotnet /tmp/dispatch-old/Dispatch.dll 200000 10 short-default true 1 200000 2 2
dotnet /tmp/dispatch-new/Dispatch.dll 200000 10 short-default true 1 200000 2 2
```

For lock timing, create two additional disposable worktrees, apply `instrument-baseline.py` only to `03273aa`, then apply `instrument-locks.py` to each. Build their libraries and the same harness with `TaskFlowAssemblyPath` pointing at those DLLs. Run `200000 4 short-default true 1 200000 2 32` in old/head/head/old order. Stderr contains one summary per warmup/sample: frequency followed by `method,acquisitions,wait_ticks,held_ticks`; exclude the first two warmups. The timestamp probe is intentionally absent from production assemblies.

For managed tracing (run separately from unprofiled measurements):

```sh
dotnet tool install dotnet-trace --version 10.0.750501 --tool-path /tmp/trace-tools
/tmp/trace-tools/dotnet-trace collect --profile dotnet-sampled-thread-time \
  --providers Microsoft-Windows-DotNETRuntime:0x14001:5 --format Speedscope \
  --output /tmp/old.nettrace -- dotnet /tmp/dispatch-old/Dispatch.dll \
  200000 4 short-default true 1 200000 2 32
/tmp/trace-tools/dotnet-trace report /tmp/old.nettrace topN -n 25 --inclusive
```

Repeat for the head. Speedscope output permits stack inspection. For aggregate contention, TraceEvent's `TraceLog.CreateFromEventPipeDataFile`/`TraceLog.Events` expose `ContentionStartTraceData` and `ContentionStopTraceData`; classify the start's `CallStack()` by its nearest `KeyedCoordinator`/`TaskSchedulerMiddlewareLifetime` frame, pair by `ThreadID`, and sum `DurationNs / 1_000_000`. The JSON stores the resulting classifications and durations. Trace binaries remain local, rather than adding large generated binaries to the repository.

## Decision and remaining limits

There is no new supported-operation correctness blocker, and no evidence-backed reason to add recovery or optimize the current architecture. The three new fault cases supplement the existing cancellation, disposal, context, FIFO, fairness, accounting, and retirement tests. Ordinary failures settle observably; catastrophic runtime failures remain explicitly outside the progress guarantee. Isolated arrivals can still require one drain each; long blocking callbacks, pool starvation, and completion handoffs still affect progress and performance. Thread-pool/GC sensitivity warrants workload-specific measurements, not special production paths.

PR #38 is ready for review/merge with these documented limits, subject to the repository's handling of existing issue #39 test failures. No merge is performed by this work.

Final validation: solution restore and Release build passed for netstandard2.0, net8.0, and net10.0 with zero warnings/errors, including analyzers/public API checks. Solution and benchmark whitespace/file-scoped namespace checks passed (the solution formatter retains its existing unsupported shared-project workspace warning). The benchmark Release build passed. Focused admission, keyed, lifetime, TrySubmit, and dispatch tests passed 138/138 on each runtime (276 total), including all six new fault cases. The full solution passed 987/990; only `DisposeAsync_ShouldWaitQueuedOperations` failed: DedicatedThreadTaskFlowFixture on both runtimes and CurrentThreadTaskFlowFixture on net10.0, at the existing `task3.IsCompleted` assertion tracked in #39. No existing assertion/test was weakened or removed. Linux x64 NativeAOT publish/trimming and execution passed with Guid keyed typed-state, tokenless Task, captured context, and suppressed-context submissions.