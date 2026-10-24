# TaskFlow main

Measured checkout: `3952bd7ff8e9f06ef8b470d2d5337e99fc3bbe63`; release source: `None`

Harness: `f1fe135c8478889cb1e8debfb957fc395949377756770326cb8f2306141e0e36`; environment series: `a36a07b838abe4200b258ca43e07034e0168ccca09e4ed48a1b924d8ef1a8ef2`

[Workflow run and downloadable raw artifacts](https://github.com/dombrovsky/TaskFlow/actions/runs/40132584345#artifacts)

Cross-run history is advisory. Inspect variability and runner configuration before comparing.

| Case | Mean ns/op | StdDev | Ops/s | Bytes/op | Samples |
|---|---:|---:|---:|---:|---:|
| TaskFlow.Benchmarks.BurstBenchmarks.SubmitAndDrain | 480.87 | 2.20 | 2079571.45 | 655 | 6 |
| TaskFlow.Benchmarks.EnqueueOnlyBenchmarks.SubmitOnly | 769.25 | 182.56 | 1299959.05 | unsupported | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 0) | 1091.95 | 8.24 | 915794.56 | 513 | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 1) | 2740.12 | 25.96 | 364947.31 | 1790 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 2) | 3312.57 | 38.78 | 301880.58 | 2416 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 4) | 4249.72 | 88.46 | 235309.83 | 3664 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 8) | 6180.21 | 226.05 | 161806.92 | 6160 | 8 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 1) | 408.75 | 13.24 | 2446482.85 | 605 | 8 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 4) | 659.72 | 65.19 | 1515792.78 | 689 | 8 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 16) | 658.64 | 42.43 | 1518270.02 | 699 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: None) | 1157.69 | 4.30 | 863792.53 | 513 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: None) | 2141.10 | 14.95 | 467049.26 | 712 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: Live) | 1279.73 | 13.96 | 781416.79 | 529 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: Live) | 2126.85 | 29.41 | 470179.18 | 728 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: CanceledCooperative) | 1194.61 | 20.03 | 837092.53 | 529 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: CanceledCooperative) | 2226.46 | 41.33 | 449143.46 | 728 | 8 |
