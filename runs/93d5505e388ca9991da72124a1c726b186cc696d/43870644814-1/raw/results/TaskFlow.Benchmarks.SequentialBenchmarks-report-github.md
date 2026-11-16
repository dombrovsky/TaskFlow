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
| **SyncCompletion**  | **None**                |   **718.5 ns** |  **6.63 ns** |  **2.94 ns** | **1,391,723.6** |  **1.00** |    **0.01** | **0.0305** |     **512 B** |        **1.00** |
| AsyncCompletion | None                | 1,242.2 ns | 39.79 ns | 20.81 ns |   805,024.0 |  1.73 |    0.03 | 0.0420 |     712 B |        1.39 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **Live**                |   **777.7 ns** | **16.89 ns** |  **8.83 ns** | **1,285,923.5** |  **1.00** |    **0.02** | **0.0315** |     **528 B** |        **1.00** |
| AsyncCompletion | Live                | 1,244.5 ns | 10.08 ns |  4.48 ns |   803,508.6 |  1.60 |    0.02 | 0.0420 |     728 B |        1.38 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **CanceledCooperative** |   **852.9 ns** | **33.47 ns** | **17.50 ns** | **1,172,428.1** |  **1.00** |    **0.03** | **0.0315** |     **528 B** |        **1.00** |
| AsyncCompletion | CanceledCooperative | 1,272.9 ns |  9.80 ns |  5.13 ns |   785,631.5 |  1.49 |    0.03 | 0.0420 |     728 B |        1.38 |
