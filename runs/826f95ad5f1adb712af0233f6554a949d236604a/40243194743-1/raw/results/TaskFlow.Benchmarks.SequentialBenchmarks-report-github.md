```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V74 2.60GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method          | Token               | Mean       | Error    | StdDev   | Op/s        | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------- |-------------------- |-----------:|---------:|---------:|------------:|------:|--------:|-------:|----------:|------------:|
| **SyncCompletion**  | **None**                |   **715.8 ns** |  **4.95 ns** |  **2.20 ns** | **1,397,111.4** |  **1.00** |    **0.00** | **0.0305** |     **512 B** |        **1.00** |
| AsyncCompletion | None                | 1,203.9 ns | 11.48 ns |  6.01 ns |   830,657.7 |  1.68 |    0.01 | 0.0420 |     712 B |        1.39 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **Live**                |   **873.2 ns** | **37.99 ns** | **19.87 ns** | **1,145,208.5** |  **1.00** |    **0.03** | **0.0315** |     **528 B** |        **1.00** |
| AsyncCompletion | Live                | 1,261.5 ns | 27.71 ns | 14.50 ns |   792,680.2 |  1.45 |    0.03 | 0.0420 |     728 B |        1.38 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **CanceledCooperative** |   **877.6 ns** | **35.64 ns** | **18.64 ns** | **1,139,460.1** |  **1.00** |    **0.03** | **0.0315** |     **528 B** |        **1.00** |
| AsyncCompletion | CanceledCooperative | 1,279.6 ns | 13.06 ns |  5.80 ns |   781,516.0 |  1.46 |    0.03 | 0.0420 |     728 B |        1.38 |
