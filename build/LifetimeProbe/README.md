# Lifetime regression probes

This standalone .NET 10 executable is outside the solution and packages. Its signed
`TaskFlow.Tests` assembly name uses the library's existing friend assembly access;
no public instrumentation APIs are added.

```sh
dotnet run --project build/LifetimeProbe -c Release -- --scope
dotnet run --project build/LifetimeProbe -c Release -- --pending
DOTNET_PROCESSOR_COUNT=2 dotnet run --project build/LifetimeProbe -c Release > samples.json
```

Build before measuring. Do not run competing builds, tests, traces, or measurements.
Use independent checkouts, identical harness sources, SDK/runtime and environment.
The scope probe measures cold registration/cleanup scopes, not enqueue overhead.
It uses thread allocation counts, one million repetitions, and a separate warmup.
The pending probe uses an inline terminal and a gate completed only after Enqueue
returns, forcing execution wrappers to suspend; it verifies that all continuations
finish inline before recording current-thread allocation totals. The scheduling probe records eight raw samples after twelve
warmup batches; each sample drains 8,192 operations and checks all results. It
records whole-process managed allocation deltas to include worker threads. Full
collections happen outside the timed interval. Owners are disposed between cases.

These are screening probes. They do not perform BenchmarkDotNet calibration,
isolate each case in its own process, or estimate confidence intervals. Tiered
compilation, scheduling and container throttling can dominate their timing.
Confirm timing conclusions with the unchanged `TaskFlow.Benchmarks` harness and
retain its full JSON. The coordinated cases cover outstanding/pending admission,
concurrent bounded producers, shared keyed scheduling and concurrent keyed
producers; they do not have a release 1.0.0 baseline.

For allocation attribution, optionally launch a prebuilt probe with
`dotnet-trace collect --profile gc-verbose -- dotnet <probe.dll>`. Allocation ticks
are samples; aggregate type weights are estimates, not exact per-operation
object counts. Keep trace runs separate from timing runs.
