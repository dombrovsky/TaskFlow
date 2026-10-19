```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 2.60GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method          | Token               | Mean       | Error    | StdDev   | Op/s        | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------- |-------------------- |-----------:|---------:|---------:|------------:|------:|--------:|-------:|----------:|------------:|
| **SyncCompletion**  | **None**                |   **892.4 ns** | **10.56 ns** |  **5.52 ns** | **1,120,633.4** |  **1.00** |    **0.01** | **0.0305** |     **512 B** |        **1.00** |
| AsyncCompletion | None                | 1,561.7 ns | 35.49 ns | 15.76 ns |   640,332.1 |  1.75 |    0.02 | 0.0381 |     712 B |        1.39 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **Live**                |   **956.1 ns** | **30.86 ns** | **16.14 ns** | **1,045,920.8** |  **1.00** |    **0.02** | **0.0315** |     **528 B** |        **1.00** |
| AsyncCompletion | Live                | 1,665.3 ns | 56.79 ns | 29.70 ns |   600,481.3 |  1.74 |    0.04 | 0.0381 |     728 B |        1.38 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **CanceledCooperative** |   **891.8 ns** | **14.36 ns** |  **6.37 ns** | **1,121,385.3** |  **1.00** |    **0.01** | **0.0305** |     **528 B** |        **1.00** |
| AsyncCompletion | CanceledCooperative | 1,557.6 ns | 57.26 ns | 25.42 ns |   641,999.8 |  1.75 |    0.03 | 0.0381 |     728 B |        1.38 |
