# TaskFlow main

Measured checkout: `5d19ecd4b6e3a8eaf11f7e185887f14bca96ff86`; release source: `None`

Harness: `f1fe135c8478889cb1e8debfb957fc395949377756770326cb8f2306141e0e36`; environment series: `4d07e191394aad06bf1d1f9b522ba6c50d4ec2cf74b42bc21956cb003f605ef7`

[Workflow run and downloadable raw artifacts](https://github.com/dombrovsky/TaskFlow/actions/runs/41354893253#artifacts)

Cross-run history is advisory. Inspect variability and runner configuration before comparing.

| Case | Mean ns/op | StdDev | Ops/s | Bytes/op | Samples |
|---|---:|---:|---:|---:|---:|
| TaskFlow.Benchmarks.BurstBenchmarks.SubmitAndDrain | 617.24 | 29.90 | 1620117.76 | 650 | 8 |
| TaskFlow.Benchmarks.EnqueueOnlyBenchmarks.SubmitOnly | 391.99 | 115.80 | 2551078.98 | unsupported | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 0) | 892.89 | 8.27 | 1119953.22 | 535 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 1) | 1576.34 | 20.88 | 634379.95 | 1606 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 2) | 1766.83 | 8.45 | 565985.41 | 2111 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 4) | 2207.45 | 11.75 | 453011.99 | 3120 | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 8) | 3052.67 | 36.09 | 327582.28 | 5136 | 8 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 1) | 416.37 | 3.92 | 2401688.87 | 624 | 7 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 4) | 668.60 | 14.45 | 1495660.27 | 665 | 7 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 16) | 681.70 | 2.68 | 1466921.86 | 664 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: None) | 862.03 | 23.35 | 1160051.17 | 539 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: None) | 1243.92 | 3.64 | 803911.53 | 724 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: Live) | 837.30 | 4.67 | 1194318.55 | 554 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: Live) | 1305.78 | 7.32 | 765823.37 | 740 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: CanceledCooperative) | 910.90 | 16.84 | 1097820.94 | 548 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: CanceledCooperative) | 1355.16 | 9.72 | 737920.48 | 736 | 7 |
