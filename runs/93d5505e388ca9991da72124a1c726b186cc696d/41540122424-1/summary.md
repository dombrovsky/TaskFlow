# TaskFlow main

Measured checkout: `93d5505e388ca9991da72124a1c726b186cc696d`; release source: `None`

Harness: `f1fe135c8478889cb1e8debfb957fc395949377756770326cb8f2306141e0e36`; environment series: `b5d811a455bb869431e320f4ecf4241bb2180cebab603fd9b3d0d4f848884127`

[Workflow run and downloadable raw artifacts](https://github.com/dombrovsky/TaskFlow/actions/runs/41540122424#artifacts)

Cross-run history is advisory. Inspect variability and runner configuration before comparing.

| Case | Mean ns/op | StdDev | Ops/s | Bytes/op | Samples |
|---|---:|---:|---:|---:|---:|
| TaskFlow.Benchmarks.BurstBenchmarks.SubmitAndDrain | 465.38 | 14.05 | 2148774.35 | 674 | 7 |
| TaskFlow.Benchmarks.EnqueueOnlyBenchmarks.SubmitOnly | 781.00 | 171.10 | 1280402.00 | unsupported | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 0) | 996.09 | 13.49 | 1003923.42 | 512 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 1) | 1937.64 | 23.08 | 516093.01 | 1607 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 2) | 2278.84 | 34.11 | 438819.10 | 2112 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 4) | 2741.96 | 14.11 | 364702.57 | 3120 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 8) | 3953.00 | 10.63 | 252972.74 | 5136 | 7 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 1) | 451.00 | 3.63 | 2217310.63 | 641 | 8 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 4) | 642.49 | 17.76 | 1556451.74 | 699 | 8 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 16) | 645.82 | 29.05 | 1548423.02 | 702 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: None) | 906.77 | 1.76 | 1102817.55 | 512 | 6 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: None) | 1554.97 | 24.49 | 643097.69 | 712 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: Live) | 982.77 | 9.32 | 1017531.01 | 528 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: Live) | 1630.30 | 2.69 | 613382.62 | 728 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: CanceledCooperative) | 954.36 | 4.82 | 1047818.29 | 528 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: CanceledCooperative) | 1606.01 | 28.56 | 622662.07 | 728 | 8 |
