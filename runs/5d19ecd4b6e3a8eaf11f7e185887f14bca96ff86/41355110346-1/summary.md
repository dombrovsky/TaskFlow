# TaskFlow release 1.1.0-rc.10

Measured checkout: `5d19ecd4b6e3a8eaf11f7e185887f14bca96ff86`; release source: `5d19ecd4b6e3a8eaf11f7e185887f14bca96ff86`

Harness: `f1fe135c8478889cb1e8debfb957fc395949377756770326cb8f2306141e0e36`; environment series: `b5d811a455bb869431e320f4ecf4241bb2180cebab603fd9b3d0d4f848884127`

[Workflow run and downloadable raw artifacts](https://github.com/dombrovsky/TaskFlow/actions/runs/41355110346#artifacts)

Cross-run history is advisory. Inspect variability and runner configuration before comparing.

| Case | Mean ns/op | StdDev | Ops/s | Bytes/op | Samples |
|---|---:|---:|---:|---:|---:|
| TaskFlow.Benchmarks.BurstBenchmarks.SubmitAndDrain | 809.27 | 13.17 | 1235683.73 | 643 | 8 |
| TaskFlow.Benchmarks.EnqueueOnlyBenchmarks.SubmitOnly | 633.40 | 240.23 | 1578781.18 | unsupported | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 0) | 998.15 | 7.72 | 1001854.60 | 538 | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 1) | 1974.51 | 17.55 | 506453.93 | 1605 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 2) | 2283.75 | 29.19 | 437877.19 | 2112 | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 4) | 2787.51 | 42.97 | 358742.58 | 3120 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 8) | 4069.82 | 18.76 | 245710.92 | 5136 | 8 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 1) | 576.96 | 7.59 | 1733213.23 | 634 | 7 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 4) | 910.43 | 12.22 | 1098385.11 | 661 | 7 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 16) | 909.94 | 5.26 | 1098971.99 | 665 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: None) | 1020.39 | 8.73 | 980014.36 | 536 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: None) | 1664.78 | 13.59 | 600679.37 | 718 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: Live) | 1119.80 | 10.30 | 893020.18 | 552 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: Live) | 1734.25 | 12.12 | 576618.11 | 734 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: CanceledCooperative) | 1015.04 | 3.68 | 985181.77 | 558 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: CanceledCooperative) | 1647.02 | 8.04 | 607156.92 | 741 | 7 |
