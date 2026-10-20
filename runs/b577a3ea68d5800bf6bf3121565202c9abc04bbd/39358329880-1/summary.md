# TaskFlow main

Measured checkout: `b577a3ea68d5800bf6bf3121565202c9abc04bbd`; release source: `None`

Harness: `f1fe135c8478889cb1e8debfb957fc395949377756770326cb8f2306141e0e36`; environment series: `019ced3f545fac084ebf458d3d4077297512b3dc3fd1b7f26b15452c8702ea29`

[Workflow run and downloadable raw artifacts](https://github.com/dombrovsky/TaskFlow/actions/runs/39358329880#artifacts)

Cross-run history is advisory. Inspect variability and runner configuration before comparing.

| Case | Mean ns/op | StdDev | Ops/s | Bytes/op | Samples |
|---|---:|---:|---:|---:|---:|
| TaskFlow.Benchmarks.BurstBenchmarks.SubmitAndDrain | 429.93 | 24.03 | 2325983.87 | 654 | 8 |
| TaskFlow.Benchmarks.EnqueueOnlyBenchmarks.SubmitOnly | 854.44 | 302.97 | 1170354.45 | unsupported | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 0) | 994.91 | 20.27 | 1005118.83 | 512 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 1) | 2413.28 | 22.43 | 414374.10 | 1791 | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 2) | 2937.76 | 23.17 | 340395.07 | 2424 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 4) | 3937.33 | 15.32 | 253978.93 | 3688 | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 8) | 6165.18 | 25.23 | 162201.21 | 6216 | 8 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 1) | 376.90 | 14.42 | 2653190.45 | 653 | 8 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 4) | 587.41 | 47.45 | 1702398.49 | 689 | 8 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 16) | 577.88 | 22.93 | 1730471.96 | 698 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: None) | 965.40 | 2.31 | 1035837.13 | 512 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: None) | 1582.77 | 25.91 | 631803.62 | 712 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: Live) | 1072.48 | 20.33 | 932419.32 | 528 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: Live) | 1642.72 | 4.69 | 608748.03 | 728 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: CanceledCooperative) | 975.78 | 5.43 | 1024819.98 | 528 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: CanceledCooperative) | 1577.73 | 7.41 | 633821.96 | 728 | 8 |
