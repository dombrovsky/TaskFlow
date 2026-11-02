# TaskFlow main

Measured checkout: `0a540e6cdc09ffeb49a6b6fdb4c16d73f7fe78f8`; release source: `None`

Harness: `f1fe135c8478889cb1e8debfb957fc395949377756770326cb8f2306141e0e36`; environment series: `4d07e191394aad06bf1d1f9b522ba6c50d4ec2cf74b42bc21956cb003f605ef7`

[Workflow run and downloadable raw artifacts](https://github.com/dombrovsky/TaskFlow/actions/runs/41534469245#artifacts)

Cross-run history is advisory. Inspect variability and runner configuration before comparing.

| Case | Mean ns/op | StdDev | Ops/s | Bytes/op | Samples |
|---|---:|---:|---:|---:|---:|
| TaskFlow.Benchmarks.BurstBenchmarks.SubmitAndDrain | 398.96 | 11.22 | 2506514.04 | 657 | 8 |
| TaskFlow.Benchmarks.EnqueueOnlyBenchmarks.SubmitOnly | 835.19 | 168.24 | 1197328.25 | unsupported | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 0) | 958.42 | 3.42 | 1043382.89 | 512 | 7 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 1) | 2055.05 | 18.98 | 486606.39 | 1605 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 2) | 2324.77 | 19.97 | 430151.00 | 2110 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 4) | 2866.77 | 52.10 | 348824.45 | 3120 | 8 |
| TaskFlow.Benchmarks.MiddlewareBenchmarks.PipelineCompletion(Layers: 8) | 4110.18 | 25.77 | 243298.21 | 5136 | 8 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 1) | 405.42 | 48.44 | 2466592.10 | 611 | 8 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 4) | 568.49 | 8.97 | 1759059.12 | 699 | 6 |
| TaskFlow.Benchmarks.ProducerBenchmarks.ConcurrentSubmitAndDrain(Producers: 16) | 574.43 | 13.11 | 1740844.92 | 698 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: None) | 965.32 | 5.74 | 1035929.05 | 512 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: None) | 1580.87 | 10.40 | 632562.37 | 712 | 8 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: Live) | 1034.60 | 4.53 | 966560.51 | 528 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: Live) | 1633.39 | 7.20 | 612223.56 | 728 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.SyncCompletion(Token: CanceledCooperative) | 1044.04 | 13.85 | 957821.93 | 528 | 7 |
| TaskFlow.Benchmarks.SequentialBenchmarks.AsyncCompletion(Token: CanceledCooperative) | 1602.75 | 31.37 | 623927.62 | 728 | 8 |
