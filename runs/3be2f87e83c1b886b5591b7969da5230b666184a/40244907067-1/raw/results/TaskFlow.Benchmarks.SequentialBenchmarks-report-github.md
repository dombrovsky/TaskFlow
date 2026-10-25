```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method          | Token               | Mean       | Error    | StdDev   | Op/s        | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------- |-------------------- |-----------:|---------:|---------:|------------:|------:|--------:|-------:|----------:|------------:|
| **SyncCompletion**  | **None**                |   **895.7 ns** |  **1.85 ns** |  **0.82 ns** | **1,116,481.1** |  **1.00** |    **0.00** | **0.0305** |     **512 B** |        **1.00** |
| AsyncCompletion | None                | 1,521.1 ns | 25.07 ns | 13.11 ns |   657,440.5 |  1.70 |    0.01 | 0.0381 |     712 B |        1.39 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **Live**                |   **931.9 ns** |  **4.99 ns** |  **2.61 ns** | **1,073,030.6** |  **1.00** |    **0.00** | **0.0305** |     **528 B** |        **1.00** |
| AsyncCompletion | Live                | 1,618.7 ns | 13.01 ns |  6.81 ns |   617,774.1 |  1.74 |    0.01 | 0.0381 |     728 B |        1.38 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **CanceledCooperative** |   **947.9 ns** | **45.82 ns** | **20.34 ns** | **1,054,987.5** |  **1.00** |    **0.03** | **0.0305** |     **528 B** |        **1.00** |
| AsyncCompletion | CanceledCooperative | 1,543.5 ns |  8.74 ns |  3.88 ns |   647,868.0 |  1.63 |    0.03 | 0.0420 |     728 B |        1.38 |
