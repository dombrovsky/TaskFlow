# TaskFlow release 1.1.0

Measured checkout: `0a540e6cdc09ffeb49a6b6fdb4c16d73f7fe78f8`; release source: `0a540e6cdc09ffeb49a6b6fdb4c16d73f7fe78f8`

Harness: `f1fe135c8478889cb1e8debfb957fc395949377756770326cb8f2306141e0e36`; environment series: `b5d811a455bb869431e320f4ecf4241bb2180cebab603fd9b3d0d4f848884127`

[Workflow run and downloadable raw artifacts](https://github.com/dombrovsky/TaskFlow/actions/runs/41534834876#artifacts)

Cross-run history is advisory. Inspect variability and runner configuration before comparing.

| Case | Mean ns/op | StdDev | Ops/s | Bytes/op | Samples |
|---|---:|---:|---:|---:|---:|
| TaskFlow.Benchmarks.BurstBenchmarks.SubmitAndDrain | 453.33 | 24.30 | 2205876.95 | 664 | 7 |
| TaskFlow.Benchmarks.EnqueueOnlyBenchmarks.SubmitOnly | 838.88 | 172.55 | 1192069.87 | unsupported | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 0) | 923.02 | 11.84 | 1083405.35 | 512 | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 1) | 1995.96 | 24.68 | 501012.73 | 1607 | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 2) | 2320.18 | 69.12 | 431000.55 | 2111 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 4) | 2796.57 | 33.72 | 357581.22 | 3120 | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 8) | 4034.16 | 11.64 | 247883.13 | 5136 | 8 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 1) | 465.84 | 18.72 | 2146670.74 | 651 | 8 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 4) | 640.33 | 25.55 | 1561704.11 | 702 | 8 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 16) | 631.15 | 10.73 | 1584414.12 | 699 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: None) | 935.02 | 21.44 | 1069494.68 | 512 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: None) | 1526.47 | 6.01 | 655106.68 | 712 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: Live) | 933.97 | 9.79 | 1070697.06 | 528 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: Live) | 1615.08 | 4.99 | 619165.69 | 728 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: CanceledCooperative) | 924.05 | 3.00 | 1082187.21 | 528 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: CanceledCooperative) | 1580.44 | 6.72 | 632734.18 | 728 | 7 |
