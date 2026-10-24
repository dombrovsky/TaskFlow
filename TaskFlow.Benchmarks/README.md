# TaskFlow benchmarks

Standalone BenchmarkDotNet 0.15.8 executable targeting .NET 10. It is intentionally outside `TaskFlow.sln`, is not a test project, and sets `IsPackable=false` and `GeneratePackageOnBuild=false`. Normal solution tests and package publication do not run or publish it. No production sources are modified.

The suite contains 16 release-compatible cases from [PR #50](https://github.com/dombrovsky/TaskFlow/pull/50), plus four keyed scheduling cases and three bounded-admission cases for APIs introduced after 1.0.0, for 23 cases total. The common benchmark sources and stable identifiers are reused by the history and comparison runners.

The commands below measure the checked-out library and all 23 cases. To measure unchanged TaskFlow 1.0.0, use `python3 build/benchmark_run.py --release-1.0.0 --output /tmp/taskflow-1.0.0-measurement` with SDK 10.0.100 and runtime 10.0.0 installed. The runner checks out the exact release commit `0800af84229d7beb3a6fa507d236d0ff533b0c92` and overlays only the common harness; no historical feature branch is required. PR benchmark CI checks out the head SHA explicitly and records it in the summary.

From the repository root, with a .NET 10 SDK and runtime installed:

```sh
dotnet --info
dotnet restore TaskFlow.Benchmarks/TaskFlow.Benchmarks.csproj
dotnet build TaskFlow.Benchmarks/TaskFlow.Benchmarks.csproj -c Release --no-restore
dotnet run --project TaskFlow.Benchmarks -c Release --no-build -- --validate
dotnet run --project TaskFlow.Benchmarks -c Release --no-build -- --list flat
dotnet run --project TaskFlow.Benchmarks -c Release --no-build -- --filter '*'
```

Use Release, no debugger, an idle machine, and a fixed power profile and CPU allocation. Record `dotnet --info`, OS, CPU, container limits, and any `DOTNET_*` settings with results. Default runtime settings apply unless explicitly recorded; do not change GC or tiered compilation settings between comparisons. For a constrained container, optionally set `DOTNET_PROCESSOR_COUNT=2` for **both build and run**, and record that setting. This changes thread-pool scheduling and must match across comparisons. Do not run tests or builds concurrently with measurements.

The default job uses one launch, three warmup iterations, eight measurement iterations, and BenchmarkDotNet's pilot calibration and overhead evaluation. For stronger statistical confidence use `--launchCount 3 --iterationCount 20`. BenchmarkDotNet exports CSV, full JSON (including measurements and environment), HTML, and Markdown under `BenchmarkDotNet.Artifacts/results/`. Class/method names and parameter names form stable identifiers. Preserve those names when comparing runs. The executable returns failure for failed reports or validation errors. The separate `Benchmarks` workflow validates and dry-runs the suite on relevant PRs; a manual dispatch measures the selected filter and uploads artifacts. Open **Actions → Benchmarks → a run → Summary** to read the result tables without downloading an artifact. The summary labels PR Dry runs as smoke checks, records the tested commit and benchmark-step outcome, and shows a message when no reports were produced. The artifact also contains the combined `summary.md`, individual HTML reports, full JSON, CSV, and logs. Shared CI runners are suitable for smoke checks and sample collection, not regression thresholds.

For a quick wiring check only (not performance evidence):

```sh
dotnet run --project TaskFlow.Benchmarks -c Release --no-build -- --job Dry --filter '*'
```

`--validate` enumerates every benchmark and parameter combination, invokes each twice with its real setup/cleanup, and checks single results and all 1,000 batch results. Rejected calls must throw `AdmissionRejectedException`; enqueue-only cleanup observes every result. Validation uses no elapsed-time assertions. Run it before collecting results.

| Identifier | Work measured | Report unit |
| --- | --- | --- |
| `SequentialBenchmarks.SyncCompletion` / `AsyncCompletion` | One enqueue and observed completion; cached `ValueTask<int>` delegate vs `Task.Yield()` delegate | Per operation, end-to-end latency |
| `BurstBenchmarks.SubmitAndDrain` | One producer submits 1,000 synchronous delegates, then awaits all | Per operation, amortized batch time |
| `EnqueueOnlyBenchmarks.SubmitOnly` | Submit 1,000 operations behind an already-entered gate; preallocated task array | Per submission, enqueue-only time |
| `ProducerBenchmarks.ConcurrentSubmitAndDrain` | 1/4/16 thread-pool producers released by one start gate, 1,000 operations total | Per operation, amortized batch time |
| `MiddlewareBenchmarks.PipelineCompletion` | Bare flow (`Layers=0`), then 1/2/4/8 registrations forwarding enqueue, execution, and completion phases | Per operation, end-to-end latency |
| `KeyedBenchmarks.DispatchAndDrain` | Yielding delegates; one shared key or 1,000 distinct keys; execution limit 1/4 | Per operation, amortized batch time |
| `AdmissionAcceptedBenchmarks.AcceptedCompletion` | Capacity-one outstanding admission, enqueue and await | Per accepted operation |
| `AdmissionRejectedBenchmarks.RejectedCompletion` | Capacity held by a gated operation; enqueue, observe and catch rejection | Per rejected attempt, including exception cost |
| `AdmissionContendedBenchmarks.WaitAndDrain` | Submit 1,000 waiters while capacity one is held, release gate, drain all | Per operation, amortized batch time including release |

Batch methods set `OperationsPerInvoke=1000`: multiply reported mean by 1,000 for whole-batch latency. The `Op/s` column (`1e9 / Mean(ns)`) gives operations/sec for drained batches, and submission attempts/sec for enqueue-only/rejection. Amortized batch time is not an individual operation's queue latency or a p95/p99 latency distribution. Producer timing includes `Task.Run`, coordination, task arrays, and drain; compare with its one-producer case. There is no guarantee of simultaneous CPU execution on a small machine.

Owners, middleware, and token sources are created in global setup and disposed after measurement. Every returned task is observed, and batches finish before the next invocation. Enqueue-only and contended admission use iteration setup/cleanup, so BenchmarkDotNet runs exactly one invocation per iteration (unroll factor one). Never override their invocation/unroll counts: the gate must be reset between batches. The gate is entered before measurement, without sleeps or assumed scheduler timing. Enqueue-only drain is in cleanup; contended admission drain is timed. Small gated iterations can trigger minimum-iteration-time warnings: increase repetitions and inspect raw samples, rather than interpreting small differences.

MemoryDiagnoser reports managed bytes per operation for completion, producer, middleware, keyed, and accepted/rejected admission cases on .NET 10, including asynchronous worker allocations. Setup allocations are excluded. Batch allocation figures include the measured arrays and `Task.WhenAll`, normalized by 1,000. Memory diagnosis is deliberately omitted for the two iteration-gated cases: setup/cleanup and deferred execution make their allocation attribution unsuitable for enqueue-only claims. No native allocation measurement is implied.

`Token=None`, `Live`, and `CanceledCooperative` reuse a source created outside measurement. Delegates deliberately tolerate cancellation; the canceled case measures passing an already-canceled caller token, not exception propagation or queue removal. Per-call source creation/registration is not included. Yielding delegates measure scheduler resumption overhead, not representative application I/O.

Optional dedicated-thread and Channel/SemaphoreSlim/task-chain comparisons are deferred. Equivalent FIFO, cancellation, failure observation, lifetime, and middleware semantics need separate validation before such comparisons can support conclusions.


## Comparing the shared cases

For manual comparisons, run the same command, SDK/runtime and environment on both source checkouts, sequentially on the same idle runner:

```sh
dotnet run --project TaskFlow.Benchmarks -c Release --no-build -- --filter '*SequentialBenchmarks*' '*BurstBenchmarks*' '*ProducerBenchmarks*' '*EnqueueOnlyBenchmarks*' '*MiddlewareBenchmarks*'
```

Use independent build/artifact directories for each checkout, match cases by class/method and parameters, and retain the full JSON/environment for both runs. The modern-only keyed/admission cases have no 1.0.0 baseline. Shared harness source and stable identifiers make the 16 common cases comparable; CPU/runtime and noise still need to be controlled. The automated pairing workflow described below selects these common sources explicitly and excludes keyed/admission cases from both builds.


## Reviewing results

Generated reports belong in workflow summaries and downloadable artifacts, rather than source control. Before interpreting a measured run:

- Check that every requested case has successful measurements and valid statistics, and that the raw JSON's actual iteration count matches the configured job. Account for outliers excluded from the reported statistics. Dry runs are smoke checks only.
- Confirm that batch times, throughput, and allocations are normalized by 1,000 operations. For enqueue-only measurements, confirm invocation count one, unroll factor one, and no allocation claim; its drain occurs outside timing.
- Record the SDK/runtime, runner, CPU quota and affinity, GC/tiering settings, and any process-priority limitations alongside the results. CPU topology alone does not establish the CPU quota available to a container.
- Inspect iteration-time warnings and variability before comparing small differences. Use more launches/iterations on a controlled machine before drawing performance conclusions; shared-runner results are not regression thresholds.


## History and release comparisons

The `Benchmark history` workflow measures the 16 common cases using SDK 10.0.100 and runtime 10.0.0. It checks out an exact source commit and overlays an explicit list of common harness files, excluding keyed/admission sources even when measuring modern TaskFlow. The 1.0.0 selection uses the release commit above and verifies its production tree. Older releases without middleware omit those five cases from both sides of comparisons.

Trusted main/release runs publish immutable JSON, raw reports, provenance, and an index to the separate `benchmark-data` branch. The existing documentation deployment serves the [benchmark history view](https://dombrovsky.github.io/TaskFlow/benchmarks/). PR history jobs validate measurements without publishing. Workflow summaries and downloadable artifacts remain available; artifact retention is finite, while the data branch retains published records.

The `Benchmark comparison` workflow supports a release tag/commit baseline or the merge base with a target branch. It measures baseline then candidate on the same runner with independent checkouts and identical common sources, SDK/runtime, and settings. Select `representative` for burst and synchronous completion cases, or `full` for all compatible common cases. Reports include time, throughput, allocation deltas, uncertainty, exclusions, and environment metadata. Shared-runner differences are advisory and do not impose percentage-based failure thresholds.

For a local comparison, with SDK 10.0.100 and runtime 10.0.0 installed:

```sh
python3 build/benchmark_compare.py --mode release --baseline 1.0.0 --candidate HEAD --scope full --output /tmp/taskflow-comparison
```

Output directories must not already exist. Keyed/admission cases are measured through the standalone 23-case suite and the `Benchmarks` workflow; they have no 1.0.0 baseline and are excluded from the history/comparison contract. Generated reports are not committed to `main`.