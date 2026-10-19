# TaskFlow main

Measured checkout: `792c3c3fa18efd25d0381d4d9acb6d702c390db1`; release source: `None`

Harness: `f1fe135c8478889cb1e8debfb957fc395949377756770326cb8f2306141e0e36`; environment series: `90a76b075c215c8fe6c83ec327a67334fd01f46bdc50d19c37ed177bc1a41f93`

[Workflow run and downloadable raw artifacts](https://github.com/dombrovsky/TaskFlow/actions/runs/39303097158#artifacts)

Cross-run history is advisory. Inspect variability and runner configuration before comparing.

| Case | Mean ns/op | StdDev | Ops/s | Bytes/op | Samples |
|---|---:|---:|---:|---:|---:|
| TaskFlow.Benchmarks.BurstBenchmarks.SubmitAndDrain | 484.53 | 10.16 | 2063868.59 | 669 | 7 |
| TaskFlow.Benchmarks.EnqueueOnlyBenchmarks.SubmitOnly | 829.04 | 145.46 | 1206211.71 | unsupported | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 0) | 947.45 | 9.22 | 1055463.72 | 512 | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 1) | 2334.87 | 5.52 | 428288.61 | 1792 | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 2) | 2798.95 | 22.48 | 357277.47 | 2424 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 4) | 3799.42 | 38.92 | 263198.09 | 3688 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 8) | 5664.10 | 51.67 | 176550.64 | 6216 | 8 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 1) | 434.51 | 7.44 | 2301424.67 | 639 | 7 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 4) | 624.62 | 12.40 | 1600963.58 | 700 | 7 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 16) | 618.12 | 9.71 | 1617800.81 | 702 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: None) | 892.35 | 5.52 | 1120633.39 | 512 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: None) | 1561.69 | 15.76 | 640332.09 | 712 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: Live) | 956.10 | 16.14 | 1045920.77 | 528 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: Live) | 1665.33 | 29.70 | 600481.35 | 728 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: CanceledCooperative) | 891.75 | 6.37 | 1121385.31 | 528 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: CanceledCooperative) | 1557.63 | 25.42 | 641999.84 | 728 | 7 |
