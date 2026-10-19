# TaskFlow main

Measured checkout: `ccd52f28fd66c323230078677d4dd2e89f883a62`; release source: `None`

Harness: `f1fe135c8478889cb1e8debfb957fc395949377756770326cb8f2306141e0e36`; environment series: `574a6e22034454c56102c1be748801e4a107cb4c1a4550b100edd6777ffd00a8`

[Workflow run and downloadable raw artifacts](https://github.com/dombrovsky/TaskFlow/actions/runs/39234399601#artifacts)

Cross-run history is advisory. Inspect variability and runner configuration before comparing.

| Case | Mean ns/op | StdDev | Ops/s | Bytes/op | Samples |
|---|---:|---:|---:|---:|---:|
| TaskFlow.Benchmarks.BurstBenchmarks.SubmitAndDrain | 308.01 | 14.06 | 3246672.02 | 901 | 7 |
| TaskFlow.Benchmarks.EnqueueOnlyBenchmarks.SubmitOnly | 404.45 | 80.96 | 2472508.36 | unsupported | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 0) | 753.15 | 27.51 | 1327751.66 | 736 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 1) | 1859.50 | 17.88 | 537777.65 | 2784 | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 2) | 2071.95 | 32.20 | 482637.54 | 3416 | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 4) | 2642.52 | 61.83 | 378427.07 | 4680 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 8) | 3795.18 | 99.28 | 263491.85 | 7208 | 8 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 1) | 339.20 | 18.19 | 2948088.18 | 880 | 8 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 4) | 434.69 | 12.37 | 2300482.68 | 928 | 8 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 16) | 448.88 | 20.29 | 2227751.69 | 933 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: None) | 689.92 | 26.57 | 1449439.05 | 736 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: None) | 1096.19 | 51.08 | 912251.35 | 944 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: Live) | 693.79 | 13.90 | 1441360.78 | 752 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: Live) | 1138.68 | 10.84 | 878209.44 | 960 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: CanceledCooperative) | 782.87 | 15.29 | 1277343.88 | 752 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: CanceledCooperative) | 1104.91 | 14.81 | 905048.20 | 960 | 8 |
