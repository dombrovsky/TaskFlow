# TaskFlow main

Measured checkout: `826f95ad5f1adb712af0233f6554a949d236604a`; release source: `None`

Harness: `f1fe135c8478889cb1e8debfb957fc395949377756770326cb8f2306141e0e36`; environment series: `94dc3fc3422f2125c441f1e025fa9b68e319082d1d1e8171d2bb1e7b810ca582`

[Workflow run and downloadable raw artifacts](https://github.com/dombrovsky/TaskFlow/actions/runs/40243194743#artifacts)

Cross-run history is advisory. Inspect variability and runner configuration before comparing.

| Case | Mean ns/op | StdDev | Ops/s | Bytes/op | Samples |
|---|---:|---:|---:|---:|---:|
| TaskFlow.Benchmarks.BurstBenchmarks.SubmitAndDrain | 319.57 | 30.39 | 3129225.17 | 672 | 8 |
| TaskFlow.Benchmarks.EnqueueOnlyBenchmarks.SubmitOnly | 647.73 | 122.96 | 1543862.68 | unsupported | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 0) | 793.57 | 11.16 | 1260130.34 | 512 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 1) | 1922.37 | 7.34 | 520191.10 | 1850 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 2) | 2291.31 | 16.60 | 436432.22 | 2478 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 4) | 3100.38 | 20.25 | 322541.21 | 3728 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 8) | 4768.63 | 42.56 | 209703.72 | 6224 | 8 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 1) | 317.15 | 15.28 | 3153096.54 | 619 | 8 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 4) | 467.48 | 25.01 | 2139123.95 | 698 | 8 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 16) | 467.91 | 15.14 | 2137144.84 | 702 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: None) | 715.76 | 2.20 | 1397111.39 | 512 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: None) | 1203.87 | 6.01 | 830657.66 | 712 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: Live) | 873.20 | 19.87 | 1145208.52 | 528 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: Live) | 1261.54 | 14.50 | 792680.17 | 728 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: CanceledCooperative) | 877.61 | 18.64 | 1139460.11 | 528 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: CanceledCooperative) | 1279.56 | 5.80 | 781516.00 | 728 | 7 |
