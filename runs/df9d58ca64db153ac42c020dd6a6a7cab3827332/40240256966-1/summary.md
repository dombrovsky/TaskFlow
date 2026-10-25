# TaskFlow main

Measured checkout: `df9d58ca64db153ac42c020dd6a6a7cab3827332`; release source: `None`

Harness: `f1fe135c8478889cb1e8debfb957fc395949377756770326cb8f2306141e0e36`; environment series: `7cfacd6bf08af8b223d56e6a00df96d56704cf910648a06c3320a34194637029`

[Workflow run and downloadable raw artifacts](https://github.com/dombrovsky/TaskFlow/actions/runs/40240256966#artifacts)

Cross-run history is advisory. Inspect variability and runner configuration before comparing.

| Case | Mean ns/op | StdDev | Ops/s | Bytes/op | Samples |
|---|---:|---:|---:|---:|---:|
| TaskFlow.Benchmarks.BurstBenchmarks.SubmitAndDrain | 252.01 | 5.00 | 3968155.53 | 654 | 7 |
| TaskFlow.Benchmarks.EnqueueOnlyBenchmarks.SubmitOnly | 404.93 | 96.73 | 2469541.30 | unsupported | 6 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 0) | 654.67 | 25.20 | 1527480.57 | 512 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 1) | 1399.46 | 14.15 | 714563.36 | 1792 | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 2) | 1638.73 | 16.13 | 610226.96 | 2416 | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 4) | 2267.24 | 58.01 | 441065.33 | 3664 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 8) | 3503.14 | 99.34 | 285458.14 | 6160 | 8 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 1) | 246.89 | 9.36 | 4050350.28 | 618 | 8 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 4) | 389.06 | 30.47 | 2570315.05 | 695 | 8 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 16) | 404.78 | 14.36 | 2470467.91 | 700 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: None) | 567.05 | 6.36 | 1763520.24 | 512 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: None) | 985.29 | 15.10 | 1014925.42 | 712 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: Live) | 629.53 | 16.04 | 1588475.24 | 528 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: Live) | 1023.80 | 3.99 | 976751.85 | 728 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: CanceledCooperative) | 622.49 | 21.08 | 1606448.43 | 528 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: CanceledCooperative) | 1011.51 | 13.31 | 988620.69 | 728 | 8 |
