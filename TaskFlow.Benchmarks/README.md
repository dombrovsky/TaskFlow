# TaskFlow 1.0.0 benchmarks

Standalone BenchmarkDotNet 0.15.8 executable targeting .NET 10. It is intentionally outside `TaskFlow.sln`, is not a test project, and sets `IsPackable=false` and `GeneratePackageOnBuild=false`. Normal solution tests and package publication do not run or publish it. No production sources are modified.

This branch, `benchmarks-1.0.0`, starts at the exact 1.0.0 release commit `0800af84229d7beb3a6fa507d236d0ff533b0c92`. Its 16 cases cover APIs present in that release: sequential completion, caller-token variants, bursts, enqueue-only submission, concurrent producers, and pipeline middleware. The modern branch (`issue-43-benchmarks`, PR #47) merges this benchmark branch with current `main`, preserves these common harness files, and adds seven keyed/admission cases for APIs introduced after 1.0.0.

The baseline PR targets `main` for review, but checking out its head runs against the unchanged release library. PR benchmark CI checks out the head SHA explicitly and records that SHA in the job summary. To reproduce the release measurements, keep this branch checked out; once it is merged into `main`, a run of `main` naturally measures the current library. See PR #47 for the current-library suite.

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

`--validate` enumerates every benchmark and parameter combination, invokes each twice with its real setup/cleanup, and checks single results and all 1,000 batch results. Enqueue-only cleanup observes every result. Validation uses no elapsed-time assertions. Run it before collecting results.

| Identifier | Work measured | Report unit |
| --- | --- | --- |
| `SequentialBenchmarks.SyncCompletion` / `AsyncCompletion` | One enqueue and observed completion; cached `ValueTask<int>` delegate vs `Task.Yield()` delegate | Per operation, end-to-end latency |
| `BurstBenchmarks.SubmitAndDrain` | One producer submits 1,000 synchronous delegates, then awaits all | Per operation, amortized batch time |
| `EnqueueOnlyBenchmarks.SubmitOnly` | Submit 1,000 operations behind an already-entered gate; preallocated task array | Per submission, enqueue-only time |
| `ProducerBenchmarks.ConcurrentSubmitAndDrain` | 1/4/16 thread-pool producers released by one start gate, 1,000 operations total | Per operation, amortized batch time |
| `MiddlewareBenchmarks.PipelineCompletion` | Bare flow (`Layers=0`), then 1/2/4/8 registrations forwarding enqueue, execution, and completion phases | Per operation, end-to-end latency |

Batch methods set `OperationsPerInvoke=1000`: multiply reported mean by 1,000 for whole-batch latency. The `Op/s` column (`1e9 / Mean(ns)`) gives operations/sec for drained batches, and submission attempts/sec for enqueue-only measurements. Amortized batch time is not an individual operation's queue latency or a p95/p99 latency distribution. Producer timing includes `Task.Run`, coordination, task arrays, and drain; compare with its one-producer case. There is no guarantee of simultaneous CPU execution on a small machine.

Owners, middleware, and token sources are created in global setup and disposed after measurement. Every returned task is observed, and batches finish before the next invocation. Enqueue-only measurements use iteration setup/cleanup, so BenchmarkDotNet runs exactly one invocation per iteration (unroll factor one). Never override their invocation/unroll counts: the gate must be reset between batches. The gate is entered before measurement, without sleeps or assumed scheduler timing. Enqueue-only drain is in cleanup. Small gated iterations can trigger minimum-iteration-time warnings: increase repetitions and inspect raw samples, rather than interpreting small differences.

MemoryDiagnoser reports managed bytes per operation for completion, producer, and middleware cases on .NET 10, including asynchronous worker allocations. Setup allocations are excluded. Batch allocation figures include the measured arrays and `Task.WhenAll`, normalized by 1,000. Memory diagnosis is deliberately omitted for the iteration-gated enqueue-only case: setup/cleanup and deferred execution make their allocation attribution unsuitable for enqueue-only claims. No native allocation measurement is implied.

`Token=None`, `Live`, and `CanceledCooperative` reuse a source created outside measurement. Delegates deliberately tolerate cancellation; the canceled case measures passing an already-canceled caller token, not exception propagation or queue removal. Per-call source creation/registration is not included. Yielding delegates measure scheduler resumption overhead, not representative application I/O.

Optional dedicated-thread and Channel/SemaphoreSlim/task-chain comparisons are deferred. Equivalent FIFO, cancellation, failure observation, lifetime, and middleware semantics need separate validation before such comparisons can support conclusions.


## Comparing the shared cases

Run the same command, SDK/runtime and environment on both branches, sequentially on the same idle runner:

```sh
dotnet run --project TaskFlow.Benchmarks -c Release --no-build -- --filter '*SequentialBenchmarks*' '*BurstBenchmarks*' '*ProducerBenchmarks*' '*EnqueueOnlyBenchmarks*' '*MiddlewareBenchmarks*'
```

Use independent build/artifact directories for each checkout, match cases by class/method and parameters, and retain the full JSON/environment for both runs. The modern-only keyed/admission cases have no 1.0.0 baseline. Shared harness source and stable identifiers make the 16 common cases comparable; CPU/runtime and noise still need to be controlled. This branch does not implement the automated pairing workflow in #44.


## Reviewing results

Generated reports belong in workflow summaries and downloadable artifacts, rather than source control. Before interpreting a measured run:

- Check that every requested case has successful measurements and valid statistics, and that the raw JSON's actual iteration count matches the configured job. Account for outliers excluded from the reported statistics. Dry runs are smoke checks only.
- Confirm that batch times, throughput, and allocations are normalized by 1,000 operations. For enqueue-only measurements, confirm invocation count one, unroll factor one, and no allocation claim; its drain occurs outside timing.
- Record the SDK/runtime, runner, CPU quota and affinity, GC/tiering settings, and any process-priority limitations alongside the results. CPU topology alone does not establish the CPU quota available to a container.
- Inspect iteration-time warnings and variability before comparing small differences. Use more launches/iterations on a controlled machine before drawing performance conclusions; shared-runner results are not regression thresholds.


## Measured history (#45)

`Benchmark history` measures the unchanged 16-case common harness on relevant main pushes, weekly, release publication, or manually. PR validation measures the release checkout with read-only permissions and uploads evidence; it cannot publish history. The independent publisher only accepts successful trusted main/release runs. Benchmark execution has `contents: read`, no persisted Git credential, and no deployment token. No production library behavior changes.

The **TaskFlow 1.0.0** selection explicitly checks out `7e3410545c4cdf38602f2cdd01b2e9e67d9bad51`, then verifies production library/annotations trees, build properties/targets and signing key against source release `0800af84229d7beb3a6fa507d236d0ff533b0c92`. A subsequent measurement of merged main is never called 1.0.0. Other release selections resolve an exact tag; main records its resolved commit. The runner overlays the same common harness into an independent worktree, leaving production sources intact. SDK **10.0.100**, runtime **10.0.0**, BenchmarkDotNet **0.15.8**, Release, processor count two, three warmups, eight actual iterations and one launch are fixed. Pilot calibration and overhead evaluation remain enabled. Gated enqueue-only invocation semantics remain unchanged.

Local reproduction (install the pinned SDK/runtime first):

```sh
python3 build/benchmark_run.py --release-1.0.0 --output /tmp/taskflow-release-measured
python3 build/benchmark_run.py --source origin/main --label 'TaskFlow main' --output /tmp/taskflow-main-measured
python3 -m unittest discover -s build -p 'test_benchmark*.py'
```

Use a fresh output directory each time. The runner removes its temporary worktree even after failure; partial raw artifacts and provenance remain available. `record.json` is created only after complete valid measured output. Raw JSON includes every warmup, pilot, overhead, actual and result measurement. Provenance captures checkout/source SHA and tree identities, harness content hash and source SHA, SDK/runtime details, OS, runner/image, CPU information, affinity, cgroup quota, relevant runtime variables, settings, timestamp and workflow run/attempt. Do not set different GC/tiering variables between runs. The shared runner may deny process priority; inspect BDN logs and uncertainty.

The `benchmark-data` branch contains schema-v1 immutable records at `runs/<measured-sha>/<run-id>-<attempt>/`, including raw JSON/CSV/HTML/logs, provenance and summaries. `index.json` discovers every run. Publication uses non-force pushes and bounded fetch/reapply retries so concurrent writers preserve both records. A retry of identical evidence is idempotent; different evidence cannot overwrite an existing key. Reruns use a new attempt key. There is **no automatic cleanup**, including for release baselines: results survive Actions artifact expiry. Future retention changes must preserve releases and index consistency. Source main and PR files contain no generated measurements.

The existing `Documentation` workflow copies the data branch into its ordinary Jekyll build and remains the only Pages deployment. A successful `Benchmark history` workflow triggers that workflow through `workflow_run` (GitHub token branch pushes alone do not trigger another workflow). It checks out trusted main and never executes downloaded benchmark artifacts. Branch/path filters prevent benchmark-data pushes from triggering source builds, releases or more benchmarks. The history page at [benchmarks](https://dombrovsky.github.io/TaskFlow/benchmarks/) includes mean time, throughput, valid allocations, variability, release labels and evidence links; incompatible harness/runtime/CPU/quota/image configurations use separate series. Missing points and unsupported allocation metrics remain visible. No fixed threshold gates performance.

Cross-run history is weaker evidence than rerunning both versions sequentially on the same runner. Prefer a dedicated runner with fixed CPU allocation, power policy and background load for long-term trends. Inspect standard deviation and actual sample counts; repeat noisy runs with more evidence before drawing conclusions. Never equate an archived release point with a controlled same-run comparison.

### Activation and live evidence

This work is based on PR #50, not #47. Merge #50 first, then this dependent change. New manual/schedule/workflow_run definitions must exist on the default branch before those triggers activate. Enable Actions read/write workflow permissions for the trusted publisher if repository policy restricts the requested `contents: write`, and configure the existing GitHub Pages environment for Actions deployments. Do not grant write permissions to measurement jobs.

After activation, dispatch **Benchmark history** on **main** with `source=1.0.0`. Verify the summary shows all 16 measured cases and the checked release tree; verify `benchmark-data/index.json` and immutable raw results, then the successful **Documentation** deployment and the explicit **TaskFlow 1.0.0** chart/table label. Dispatch `source=main` to establish current-main history. A PR measurement or local run alone does not establish durable publication. Until these live steps succeed, the baseline must not be described as published. Rerun a failed measurement for fresh evidence; rerun only publication if measurement artifacts remain available. Artifacts are convenience downloads, never the durable database.

## Controlled same-run comparisons (#44)

`Benchmark comparison` starts manually with `mode=release` or `mode=merge-base`. The initial release mapping `1.0.0` resolves exactly to `0800af84229d7beb3a6fa507d236d0ff533b0c92`. Other selections accept an explicit release tag or full source SHA; there is no moving latest baseline. Merge-base records both resolved candidate and target SHAs and computes their Git merge-base. Candidate and baseline use identical common harness files, SDK/runtime, processor count and job settings, with independent worktrees, output trees and raw artifacts. Both run sequentially **baseline first, then candidate** on one runner. This ordering can introduce temporal drift; repeat runs with a stable idle runner before attribution. PR runs for comparison/reporting changes are read-only advisory validation; no required PR gate is introduced.

```sh
python3 build/benchmark_compare.py --mode release --baseline 1.0.0 --candidate origin/main --scope representative --output /tmp/release-pair
python3 build/benchmark_compare.py --mode merge-base --target origin/main --candidate HEAD --scope full --output /tmp/merge-base-pair
```

The representative scope measures the drained burst and three synchronous caller-token cases. Full scope selects all 16 compatible cases. Known keyed/bounded admission cases are explicitly outside this common harness. If a source lacks middleware, all five middleware cases are removed from **both** builds without modifying supported workloads or identifiers, and exclusions are listed. Older releases with the incompatible ValueTask/no-state Enqueue API cannot support these workload semantics; reports say so rather than adapting the workload silently. Build/runtime failures remain visible as failed workflow jobs with partial evidence, while valid candidate measurements are retained. A missing case or mismatched environment never receives a comparison delta.

Job summaries show both means, standard deviations, throughput, managed allocations and absolute/percentage changes. Zero baselines have no percentage; missing/invalid allocations are unsupported. The initial material-change heuristic is **20%**, with at least six retained samples, coefficient of variation no greater than 15%, and a difference larger than three combined standard errors. This is a conservative reporting heuristic, not a formal significance test. High variance, overlapping uncertainty and small sample sets are explicitly insufficient evidence. The workflow fails only on infrastructure/measurement errors, never because timing or allocation changed. Full raw BDN JSON and provenance are in the downloadable `benchmark-comparison` artifact and `comparison.json`; summary tables are directly visible without a download. Rerun with a fresh output directory; investigate runner load and repeat on a controlled machine.

Validation commands (generated evidence stays outside source control):

```sh
python3 build/benchmark_compare.py --mode release --baseline 1.0.0 --candidate 0800af84229d7beb3a6fa507d236d0ff533b0c92 --output /tmp/unchanged-pair
python3 build/benchmark_compare.py --mode merge-base --target 0800af84229d7beb3a6fa507d236d0ff533b0c92 --candidate 0800af84229d7beb3a6fa507d236d0ff533b0c92 --validate-slowdown --output /tmp/synthetic-pair
python3 build/benchmark_compare.py --mode release --baseline 1.0.0-rc2 --candidate HEAD --output /tmp/older-release-pair
python3 build/benchmark_compare.py --mode release --baseline 0e597547a98ec96b5e027ea7079e3876cb149432 --candidate 0800af84229d7beb3a6fa507d236d0ff533b0c92 --scope full --output /tmp/pre-middleware-pair
```

`--validate-slowdown` is only an experiment: inject an uncommitted `SpinWait(5000)` into the disposable candidate checkout, preserve the exact diff in provenance, then delete that checkout. The common harness remains identical. The record is marked synthetic, release verification is disabled for that candidate, and history publication explicitly rejects it. No production edit or generated performance result is committed. These validation results are not release performance claims.