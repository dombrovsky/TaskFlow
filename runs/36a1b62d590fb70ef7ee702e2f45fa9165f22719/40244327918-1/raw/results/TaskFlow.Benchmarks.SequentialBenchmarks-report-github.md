```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V74 3.69GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method          | Token               | Mean       | Error    | StdDev   | Op/s        | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------- |-------------------- |-----------:|---------:|---------:|------------:|------:|--------:|-------:|----------:|------------:|
| **SyncCompletion**  | **None**                |   **662.8 ns** |  **8.61 ns** |  **4.50 ns** | **1,508,720.4** |  **1.00** |    **0.01** | **0.0305** |     **512 B** |        **1.00** |
| AsyncCompletion | None                | 1,168.5 ns | 19.66 ns | 10.28 ns |   855,805.1 |  1.76 |    0.02 | 0.0420 |     712 B |        1.39 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **Live**                |   **720.6 ns** |  **9.36 ns** |  **4.90 ns** | **1,387,749.5** |  **1.00** |    **0.01** | **0.0305** |     **528 B** |        **1.00** |
| AsyncCompletion | Live                | 1,199.1 ns |  7.98 ns |  4.17 ns |   833,926.0 |  1.66 |    0.01 | 0.0420 |     728 B |        1.38 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **CanceledCooperative** |   **787.2 ns** | **32.71 ns** | **17.11 ns** | **1,270,248.3** |  **1.00** |    **0.03** | **0.0315** |     **528 B** |        **1.00** |
| AsyncCompletion | CanceledCooperative | 1,259.6 ns | 25.03 ns | 11.11 ns |   793,878.5 |  1.60 |    0.04 | 0.0420 |     728 B |        1.38 |
