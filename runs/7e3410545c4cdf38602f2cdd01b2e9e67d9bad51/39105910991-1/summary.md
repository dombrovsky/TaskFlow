# TaskFlow 1.0.0

Measured checkout: `7e3410545c4cdf38602f2cdd01b2e9e67d9bad51`; release source: `7e3410545c4cdf38602f2cdd01b2e9e67d9bad51`

Harness: `f1fe135c8478889cb1e8debfb957fc395949377756770326cb8f2306141e0e36`; environment series: `6695bb663c9c3090f3b22cea7c44fa9f3b09b93b5ac68a9a7cb1674dd5b857df`

[Workflow run and downloadable raw artifacts](https://github.com/dombrovsky/TaskFlow/actions/runs/39105910991#artifacts)

Cross-run history is advisory. Inspect variability and runner configuration before comparing.

| Case | Mean ns/op | StdDev | Ops/s | Bytes/op | Samples |
|---|---:|---:|---:|---:|---:|
| TaskFlow.Benchmarks.BurstBenchmarks.SubmitAndDrain | 290.41 | 5.53 | 3443360.61 | 655 | 8 |
| TaskFlow.Benchmarks.EnqueueOnlyBenchmarks.SubmitOnly | 687.26 | 152.60 | 1455057.33 | unsupported | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 0) | 768.34 | 4.05 | 1301509.57 | 512 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 1) | 1493.67 | 22.37 | 669490.94 | 1573 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 2) | 1689.50 | 24.22 | 591892.23 | 2055 | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 4) | 2041.49 | 9.21 | 489837.80 | 3016 | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 8) | 2927.69 | 4.88 | 341566.16 | 4936 | 7 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 1) | 291.08 | 12.75 | 3435457.19 | 659 | 8 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 4) | 430.53 | 8.42 | 2322741.48 | 697 | 8 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 16) | 440.62 | 39.18 | 2269552.12 | 698 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: None) | 720.42 | 3.94 | 1388069.63 | 512 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: None) | 1178.77 | 2.02 | 848340.95 | 712 | 6 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: Live) | 758.51 | 1.34 | 1318366.81 | 528 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: Live) | 1233.82 | 4.45 | 810493.27 | 728 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: CanceledCooperative) | 741.58 | 1.16 | 1348479.80 | 528 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: CanceledCooperative) | 1225.81 | 3.97 | 815788.69 | 728 | 7 |
