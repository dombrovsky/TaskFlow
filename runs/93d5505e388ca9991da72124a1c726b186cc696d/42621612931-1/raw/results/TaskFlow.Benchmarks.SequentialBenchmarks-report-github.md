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
| **SyncCompletion**  | **None**                |   **973.4 ns** | **30.74 ns** | **16.08 ns** | **1,027,376.1** |  **1.00** |    **0.02** | **0.0305** |     **512 B** |        **1.00** |
| AsyncCompletion | None                | 1,555.8 ns |  9.33 ns |  4.14 ns |   642,752.5 |  1.60 |    0.03 | 0.0381 |     712 B |        1.39 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **Live**                |   **971.6 ns** |  **3.22 ns** |  **1.43 ns** | **1,029,264.4** |  **1.00** |    **0.00** | **0.0305** |     **528 B** |        **1.00** |
| AsyncCompletion | Live                | 1,641.5 ns | 35.81 ns | 18.73 ns |   609,196.1 |  1.69 |    0.02 | 0.0381 |     728 B |        1.38 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **CanceledCooperative** |   **935.9 ns** | **20.75 ns** | **10.85 ns** | **1,068,462.0** |  **1.00** |    **0.02** | **0.0305** |     **528 B** |        **1.00** |
| AsyncCompletion | CanceledCooperative | 1,588.2 ns | 17.86 ns |  9.34 ns |   629,630.3 |  1.70 |    0.02 | 0.0381 |     728 B |        1.38 |
