```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 2.66GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method          | Token               | Mean       | Error    | StdDev   | Op/s        | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------- |-------------------- |-----------:|---------:|---------:|------------:|------:|--------:|-------:|----------:|------------:|
| **SyncCompletion**  | **None**                |   **906.8 ns** |  **4.95 ns** |  **1.76 ns** | **1,102,817.5** |  **1.00** |    **0.00** | **0.0305** |     **512 B** |        **1.00** |
| AsyncCompletion | None                | 1,555.0 ns | 46.83 ns | 24.49 ns |   643,097.7 |  1.71 |    0.03 | 0.0381 |     712 B |        1.39 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **Live**                |   **982.8 ns** | **21.00 ns** |  **9.32 ns** | **1,017,531.0** |  **1.00** |    **0.01** | **0.0305** |     **528 B** |        **1.00** |
| AsyncCompletion | Live                | 1,630.3 ns |  5.15 ns |  2.69 ns |   613,382.6 |  1.66 |    0.01 | 0.0420 |     728 B |        1.38 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **CanceledCooperative** |   **954.4 ns** |  **9.21 ns** |  **4.82 ns** | **1,047,818.3** |  **1.00** |    **0.01** | **0.0315** |     **528 B** |        **1.00** |
| AsyncCompletion | CanceledCooperative | 1,606.0 ns | 54.60 ns | 28.56 ns |   622,662.1 |  1.68 |    0.03 | 0.0381 |     728 B |        1.38 |
