# TaskFlow main

Measured checkout: `d95b9711844ff0df0f4b2d7c9b3ab1db3539e23f`; release source: `None`

Harness: `f1fe135c8478889cb1e8debfb957fc395949377756770326cb8f2306141e0e36`; environment series: `026d3edebb9c2568c46241d15bae08eeb26044fb773763a4e86cc36eeae9b048`

[Workflow run and downloadable raw artifacts](https://github.com/dombrovsky/TaskFlow/actions/runs/40600990476#artifacts)

Cross-run history is advisory. Inspect variability and runner configuration before comparing.

| Case | Mean ns/op | StdDev | Ops/s | Bytes/op | Samples |
|---|---:|---:|---:|---:|---:|
| TaskFlow.Benchmarks.BurstBenchmarks.SubmitAndDrain | 571.44 | 39.89 | 1749965.36 | 656 | 8 |
| TaskFlow.Benchmarks.EnqueueOnlyBenchmarks.SubmitOnly | 741.60 | 57.06 | 1348432.57 | unsupported | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 0) | 1273.88 | 23.08 | 785003.54 | 513 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 1) | 2461.12 | 21.19 | 406318.44 | 1605 | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 2) | 2655.44 | 11.88 | 376585.61 | 2110 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 4) | 3318.10 | 24.06 | 301377.02 | 3120 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 8) | 4448.94 | 40.55 | 224772.77 | 5136 | 8 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 1) | 428.98 | 6.07 | 2331121.96 | 614 | 6 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 4) | 728.14 | 72.63 | 1373354.96 | 695 | 8 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 16) | 674.00 | 20.47 | 1483683.06 | 700 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: None) | 1198.80 | 14.74 | 834168.03 | 513 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: None) | 2214.08 | 36.60 | 451654.21 | 712 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: Live) | 1223.62 | 5.92 | 817245.08 | 529 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: Live) | 2241.01 | 20.02 | 446227.35 | 728 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: CanceledCooperative) | 1244.79 | 9.41 | 803349.53 | 529 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: CanceledCooperative) | 2350.18 | 13.65 | 425498.75 | 728 | 7 |
