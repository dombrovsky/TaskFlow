# TaskFlow main

Measured checkout: `3be2f87e83c1b886b5591b7969da5230b666184a`; release source: `None`

Harness: `f1fe135c8478889cb1e8debfb957fc395949377756770326cb8f2306141e0e36`; environment series: `b61eca56856c6d8ab2b141774b9168de1bbf4d07bdac3c8519d145086a798267`

[Workflow run and downloadable raw artifacts](https://github.com/dombrovsky/TaskFlow/actions/runs/40305980849#artifacts)

Cross-run history is advisory. Inspect variability and runner configuration before comparing.

| Case | Mean ns/op | StdDev | Ops/s | Bytes/op | Samples |
|---|---:|---:|---:|---:|---:|
| TaskFlow.Benchmarks.BurstBenchmarks.SubmitAndDrain | 461.82 | 10.97 | 2165364.41 | 665 | 7 |
| TaskFlow.Benchmarks.EnqueueOnlyBenchmarks.SubmitOnly | 803.19 | 137.47 | 1245034.65 | unsupported | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 0) | 1001.90 | 19.11 | 998108.31 | 512 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 1) | 2291.96 | 15.54 | 436308.15 | 1727 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 2) | 2856.26 | 7.26 | 350107.79 | 2352 | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 4) | 3795.77 | 24.24 | 263451.04 | 3600 | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 8) | 5875.95 | 119.07 | 170185.16 | 6096 | 8 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 1) | 479.80 | 21.50 | 2084213.15 | 638 | 7 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 4) | 610.91 | 6.45 | 1636901.54 | 696 | 6 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 16) | 669.34 | 36.90 | 1494009.33 | 696 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: None) | 924.20 | 7.53 | 1082016.59 | 512 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: None) | 1535.55 | 27.00 | 651231.47 | 712 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: Live) | 969.25 | 3.64 | 1031727.38 | 528 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: Live) | 1668.06 | 5.03 | 599497.75 | 728 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: CanceledCooperative) | 945.01 | 14.67 | 1058184.26 | 528 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: CanceledCooperative) | 1575.69 | 23.05 | 634641.29 | 728 | 7 |
