```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 2.59GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method          | Token               | Mean       | Error    | StdDev   | Op/s        | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------- |-------------------- |-----------:|---------:|---------:|------------:|------:|--------:|-------:|----------:|------------:|
| **SyncCompletion**  | **None**                |   **924.2 ns** |  **6.16 ns** |  **2.74 ns** | **1,082,072.6** |  **1.00** |    **0.00** | **0.0305** |     **512 B** |        **1.00** |
| AsyncCompletion | None                | 1,589.2 ns | 42.46 ns | 15.14 ns |   629,247.7 |  1.72 |    0.02 | 0.0381 |     712 B |        1.39 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **Live**                |   **953.5 ns** | **13.30 ns** |  **6.96 ns** | **1,048,793.6** |  **1.00** |    **0.01** | **0.0305** |     **528 B** |        **1.00** |
| AsyncCompletion | Live                | 1,668.2 ns | 59.98 ns | 31.37 ns |   599,432.4 |  1.75 |    0.03 | 0.0381 |     728 B |        1.38 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **CanceledCooperative** |   **966.0 ns** | **25.95 ns** | **11.52 ns** | **1,035,196.1** |  **1.00** |    **0.02** | **0.0305** |     **528 B** |        **1.00** |
| AsyncCompletion | CanceledCooperative | 1,612.1 ns | 14.59 ns |  5.20 ns |   620,297.5 |  1.67 |    0.02 | 0.0420 |     728 B |        1.38 |
