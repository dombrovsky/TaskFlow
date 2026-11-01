```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 3.05GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method          | Token               | Mean     | Error     | StdDev    | Op/s      | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------- |-------------------- |---------:|----------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| **SyncCompletion**  | **None**                | **1.020 μs** | **0.0197 μs** | **0.0087 μs** | **980,014.4** |  **1.00** |    **0.01** | **0.0305** |     **536 B** |        **1.00** |
| AsyncCompletion | None                | 1.665 μs | 0.0306 μs | 0.0136 μs | 600,679.4 |  1.63 |    0.02 | 0.0381 |     718 B |        1.34 |
|                 |                     |          |           |           |           |       |         |        |           |             |
| **SyncCompletion**  | **Live**                | **1.120 μs** | **0.0232 μs** | **0.0103 μs** | **893,020.2** |  **1.00** |    **0.01** | **0.0324** |     **552 B** |        **1.00** |
| AsyncCompletion | Live                | 1.734 μs | 0.0273 μs | 0.0121 μs | 576,618.1 |  1.55 |    0.02 | 0.0381 |     734 B |        1.33 |
|                 |                     |          |           |           |           |       |         |        |           |             |
| **SyncCompletion**  | **CanceledCooperative** | **1.015 μs** | **0.0083 μs** | **0.0037 μs** | **985,181.8** |  **1.00** |    **0.00** | **0.0305** |     **558 B** |        **1.00** |
| AsyncCompletion | CanceledCooperative | 1.647 μs | 0.0181 μs | 0.0080 μs | 607,156.9 |  1.62 |    0.01 | 0.0381 |     741 B |        1.33 |
