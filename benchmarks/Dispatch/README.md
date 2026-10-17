# Synchronous admission dispatch measurement

This lightweight harness compares PR #38's original per-submission `Task.Run(Pump)` implementation (`03273aa`) with the guarded drain in this directory's revision. It measures end-to-end submission, execution, completion, and disposal, not just admission latency. It is deliberately outside the solution and adds no package dependency or public API.

## Method

Measured on Linux x64, .NET SDK 10.0.402 / runtime 10.0.13, with a two-CPU container quota. Each process sets the thread-pool minimum to 32 workers and I/O threads. Each scenario gets a 1,000-operation warmup followed by five 20,000-operation samples, with full collection before each sample. Four processes ran sequentially in old/new/new/old order, giving ten samples per variant and scenario. No builds or tests ran during measurement. The shared host produces substantial scheduling noise; these samples are descriptive, not a statistical performance guarantee.

Each operation returns a completed `ValueTask<int>`. All submissions must be accepted and every result is checked. One or eight producers submit against outstanding capacity equal to the operation count. Keyed cases use distinct integer keys and an execution limit equal to that count, to isolate dispatch from key/concurrency blocking. Non-keyed cases use a scheduler adapter and an explicit lifetime. Both variants use the identical harness. `burst-inline` holds the first custom scheduler `QueueTask` call until the remaining producers finish submitting; the two `short-*` cases let dispatch race producers, using either an inline custom scheduler or the default scheduler.

Throughput includes draining all accepted completions and disposing owners. Allocation uses process-wide `GC.GetTotalAllocatedBytes(precise: true)` across that interval, divided by operation count: it includes terminal tasks, contexts, promises, and some harness continuations, rather than claiming to isolate admission allocations. Producer tasks and completion storage are created before the interval. The baseline instrumentation only increments a counter before each original `Task.Run(Pump)`; it does not change scheduling. The new counter increments under the existing scheduling lock. Reflection reads the internal counter after measurement; the library contains no reflection or dynamic code for this diagnostic.

## Initial results at 7fa12ae

Values below are medians of ten samples. Drain counts are per 20,000 accepted operations; fractional medians are the midpoint of the two central sample counts. Raw samples are in [results-before.csv](results-before.csv) and [results-after.csv](results-after.csv).

| Scenario | Keyed | Producers | Operations/s before → after | Bytes/op before → after | Drains before → after |
| --- | --- | ---: | ---: | ---: | ---: |
| burst-inline | false | 1 | 98,263.5 → 121,300 | 2,815.8 → 2,615.6 | 20,000 → 1 |
| burst-inline | false | 8 | 142,998 → 140,663.5 | 2,768.2 → 2,586.6 | 20,000 → 1 |
| burst-inline | true | 1 | 70,661.5 → 86,888.5 | 4,301.6 → 3,657.1 | 20,000 → 1 |
| burst-inline | true | 8 | 76,366.5 → 102,104.5 | 4,305.5 → 3,657.6 | 20,000 → 1 |
| short-inline | false | 1 | 620,250 → 619,527.5 | 2,720.9 → 2,539.6 | 20,000 → 1,364.5 |
| short-inline | false | 8 | 193,153.5 → 263,678 | 2,750.4 → 2,547.2 | 20,000 → 1 |
| short-inline | true | 1 | 165,106.5 → 223,408 | 4,241.4 → 3,577.8 | 20,000 → 4.5 |
| short-inline | true | 8 | 91,898.5 → 126,276.5 | 4,259.1 → 3,617.3 | 20,000 → 1 |
| short-default | false | 1 | 374,831 → 584,753 | 3,138.1 → 2,948.2 | 20,000 → 530.5 |
| short-default | false | 8 | 196,403.5 → 218,623 | 3,150.8 → 2,960.4 | 20,000 → 1 |
| short-default | true | 1 | 158,075.5 → 133,725.5 | 4,930.6 → 4,274 | 20,000 → 11 |
| short-default | true | 8 | 87,304.5 → 98,173.5 | 4,930.1 → 4,287.2 | 20,000 → 1 |

The staged burst consistently reduced drain work items from 20,000 to one. Measured allocations fell in all twelve cases (about 180–200 bytes/op for non-keyed cases and 640–660 bytes/op for keyed cases). Throughput was mixed: the non-keyed eight-producer staged burst was 1.6% lower, and the keyed single-producer default-scheduler case was 15.4% lower. Other median changes ranged from approximately unchanged to 56% higher. Sample ranges are wide and often overlap—for the latter regression, before ranged from 109,008–351,754 operations/s and after from 97,453–378,543. These measurements support fewer drain items and lower allocation in this workload, not a universal throughput improvement.

Remaining costs include normal terminal task scheduling and asynchronous promise continuations. Separate idle-to-runnable transitions may still require separate drains, including one transition per operation for isolated producers. Rejections or pre-execution cancellations with completion middleware retain their per-operation asynchronous callback handoff; those cases are not measured here. Busy-key, saturated-limit, shutdown, and lost-wakeup behavior is tested deterministically in `DispatchCoalescingFixture`, rather than inferred from throughput.

The focused ownership audit and repeated keyed/default-scheduler measurements are in [REVIEW.md](REVIEW.md). They include distributions, runtime contention traces, lock timing, and a worker-minimum sensitivity check. The initial samples above remain unchanged.

## Reproduce

Run from the repository root with .NET 10 and a clean disposable baseline worktree. Restore/build both variants before measuring; keep other builds/tests idle. The baseline script refuses to instrument a worktree twice.

```sh
git worktree add --detach /tmp/taskflow-dispatch-baseline 03273aa
python3 benchmarks/Dispatch/instrument-baseline.py /tmp/taskflow-dispatch-baseline
dotnet build /tmp/taskflow-dispatch-baseline/TaskFlow/TaskFlow.csproj -c Release -f net10.0
dotnet build TaskFlow/TaskFlow.csproj -c Release -f net10.0
dotnet build benchmarks/Dispatch/Dispatch.csproj -c Release -o /tmp/dispatch-old \
  -p:TaskFlowAssemblyPath=/tmp/taskflow-dispatch-baseline/TaskFlow/bin/Release/net10.0/TaskFlow.dll
dotnet build benchmarks/Dispatch/Dispatch.csproj -c Release -o /tmp/dispatch-new

dotnet /tmp/dispatch-old/Dispatch.dll 20000 5 > /tmp/before-a.csv
dotnet /tmp/dispatch-new/Dispatch.dll 20000 5 > /tmp/after-a.csv
dotnet /tmp/dispatch-new/Dispatch.dll 20000 5 > /tmp/after-b.csv
dotnet /tmp/dispatch-old/Dispatch.dll 20000 5 > /tmp/before-b.csv
```

Arguments are operations, samples, optional scenario (`all`/`short-default`/`short-inline`/`burst-inline`), optional keyed mode (`all`/`true`/`false`), optional producer count (`all`/`1`/`8`), warmup operations, warmup rounds, and minimum worker threads. Defaults preserve the original matrix, 1,000-operation warmup, and 32-worker minimum. For example:

```sh
dotnet /tmp/dispatch-new/Dispatch.dll 200000 12 short-default true 1 200000 3 32
```

The first eight CSV columns identify scenario, keyed mode, producer count, round, operation count, operations/s, bytes/op, and scheduled drain count. Additional diagnostics report process CPU milliseconds, process-wide contended lock acquisitions, completed thread-pool work items, ending worker count, and Gen0/1/2 collections during the sample. Runtime counters include framework/harness work and do not isolate coordinator locks. Configuration is written to stderr. Initial checked-in result files add a `batch` column; review results also identify the series and variant.

`instrument-locks.py` is optional, intrusive timing instrumentation for disposable worktrees. It measures coordinator critical-section wall time and acquisition time with stack-only scopes and timestamps. It changes timing and is never used for production throughput claims; the harness reads its private summary after the measured interval. See the review for commands and limitations.