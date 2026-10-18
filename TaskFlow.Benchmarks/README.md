# TaskFlow benchmarks

Standalone BenchmarkDotNet 0.15.8 executable targeting .NET 10. It is intentionally outside `TaskFlow.sln`, is not a test project, and sets `IsPackable=false` and `GeneratePackageOnBuild=false`. Normal solution tests and package publication do not run or publish it. No production sources are modified.

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

Optional dedicated-thread and Channel/SemaphoreSlim/task-chain comparisons are deferred. Equivalent FIFO, cancellation, failure observation, lifetime, and middleware semantics need separate validation before such comparisons can support conclusions. See [samples](samples/README.md) for the local run and methodological review.


## History and release comparisons

The current workflow provides a readable report per run and downloadable raw data. It does not yet maintain a historical database or calculate release comparisons. Actions artifacts have finite retention, so they should not be the only copy of long-term results.

Recommended follow-up work:

1. **History on main and releases.** Run measured benchmarks after relevant merges to `main` and on releases. Store immutable JSON records keyed by commit SHA, release tag, benchmark-harness version, SDK/runtime, runner identity, and job settings. Use a dedicated `benchmark-data` branch or durable object storage, and publish a trend page through the existing documentation/Pages deployment. Keep this publication restricted to trusted main/release runs; PR smoke runs must not update baselines. The data branch can receive generated commits without adding bot commits to `main` or triggering source build/release workflows. Include a schema version and retain raw measurements alongside summaries.
2. **Comparison with a released version.** Add a manual baseline-release input, resolve it to an exact tag/commit or NuGet version, and run the same benchmark harness against both candidate and release on the same runner with the same SDK/runtime and settings. Run the two sequentially with independent build/output directories. Report mean time, throughput and allocation changes for matching cases, together with uncertainty and environment metadata; do not fail builds on noisy shared-runner percentages. Older releases may not have middleware, keyed, or admission APIs: use a compatible common subset and mark missing cases unsupported. Running different historical harness versions and comparing their numbers is not equivalent.

A one-off comparison can be collected today by running an unchanged, compatible benchmark harness against both library versions on the same idle machine and comparing the exported tables/JSON. Pin the release version explicitly and keep both sets of artifacts. A workflow that performs this pairing and publishes the delta table is a separate follow-up to this suite.

Avoid automatically committing a fresh Markdown report to `main` after every merge. That creates source-history churn, requires write permissions on the protected branch, and can cause CI loops. A per-run job summary plus immutable results on a separate data branch and a Pages history view gives readable reports and durable history without changing source commits. The repository already has a documentation Pages deployment; integrate the history view there rather than creating a second deployment that overwrites it.