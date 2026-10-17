# Local sample and methodology review

Collected on 2026-10-17 in the managed Linux container, after builds, formatting, and tests had exited. See [environment.txt](environment.txt) for the .NET SDK/runtime, production baseline commit, and settings. The container had a two-CPU quota, an affinity mask of three logical CPUs, 8 GiB memory, and `DOTNET_PROCESSOR_COUNT=2`. BenchmarkDotNet's CPU topology display describes the affinity mask, not the quota. GC and tiered-compilation overrides were unset.

From the repository root, after the Release build and `--validate` commands in the parent README:

```sh
export DOTNET_PROCESSOR_COUNT=2
export DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet run --project TaskFlow.Benchmarks -c Release --no-build -- --filter '*BurstBenchmarks*' '*EnqueueOnlyBenchmarks*' '*AdmissionRejectedBenchmarks*' --artifacts /tmp/taskflow-benchmark-sample
```

The checked-in CSV, Markdown, full JSON, and `run.txt` are the BenchmarkDotNet exports/output, with only indentation, line endings, and trailing whitespace normalized for these three representative cases. The default job used one launch, three warmup iterations and eight measurement iterations. Full JSON retains raw measurements and statistics; use `FullName` as the stable case identifier. The complete set of 23 parameterized cases also passed `--validate` (two invocations each) and a separate Dry run. Dry measurements are intentionally not published as performance evidence.

Methodology review:

- Both batches report 1,000 operations per invocation; times and throughput are normalized per operation. `SubmitAndDrain` includes result-array allocation and observation of all 1,000 completions. `SubmitOnly` holds its queue gate throughout measurement, and observes all results during cleanup.
- `SubmitOnly` has invocation count one and unroll factor one. Its allocation field is absent, as intended. Its short iterations trigger BenchmarkDotNet's minimum-iteration-time warning; these samples do not support fine-grained enqueue latency comparisons.
- Rejection keeps capacity occupied until global cleanup. Every measured attempt catches the rejection; it cannot accidentally measure accepted work. Its throughput counts rejected attempts, and its managed allocation figure includes exceptions and observation.
- All three reports contain statistics and successful measurements. Normal measurement cases have eight actual workload iterations; BenchmarkDotNet may remove outliers when computing statistics. Setup/disposal are outside timing, and no tasks or closed gates survive cleanup.
- The container cannot raise process priority. Together with the shared host, short gated iterations, one launch, and only eight measurements, this limits statistical confidence. These files demonstrate functioning exports, correct units and workload boundaries; they are not performance targets or regression thresholds. Use more launches/iterations on a controlled machine before drawing conclusions.