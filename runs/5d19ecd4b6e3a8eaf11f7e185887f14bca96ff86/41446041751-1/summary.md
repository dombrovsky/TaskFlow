# TaskFlow main

Measured checkout: `5d19ecd4b6e3a8eaf11f7e185887f14bca96ff86`; release source: `None`

Harness: `f1fe135c8478889cb1e8debfb957fc395949377756770326cb8f2306141e0e36`; environment series: `4d07e191394aad06bf1d1f9b522ba6c50d4ec2cf74b42bc21956cb003f605ef7`

[Workflow run and downloadable raw artifacts](https://github.com/dombrovsky/TaskFlow/actions/runs/41446041751#artifacts)

Cross-run history is advisory. Inspect variability and runner configuration before comparing.

| Case | Mean ns/op | StdDev | Ops/s | Bytes/op | Samples |
|---|---:|---:|---:|---:|---:|
| TaskFlow.Benchmarks.BurstBenchmarks.SubmitAndDrain | 618.58 | 15.98 | 1616597.72 | 649 | 8 |
| TaskFlow.Benchmarks.EnqueueOnlyBenchmarks.SubmitOnly | 350.50 | 110.50 | 2853069.95 | unsupported | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 0) | 860.77 | 15.41 | 1161749.47 | 536 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 1) | 1636.15 | 18.78 | 611189.80 | 1603 | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 2) | 1850.07 | 28.65 | 540519.30 | 2109 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 4) | 2269.62 | 10.41 | 440602.52 | 3119 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 8) | 3160.30 | 31.55 | 316425.32 | 5136 | 7 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 1) | 408.63 | 2.93 | 2447176.26 | 622 | 7 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 4) | 685.18 | 13.38 | 1459460.61 | 664 | 7 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 16) | 728.87 | 29.78 | 1371995.18 | 664 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: None) | 825.03 | 12.73 | 1212084.14 | 539 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: None) | 1289.93 | 9.51 | 775238.53 | 723 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: Live) | 829.86 | 6.04 | 1205028.73 | 556 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: Live) | 1296.69 | 2.44 | 771194.52 | 742 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: CanceledCooperative) | 848.62 | 2.16 | 1178383.91 | 551 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: CanceledCooperative) | 1348.05 | 5.80 | 741813.24 | 731 | 8 |
