```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V74 2.60GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method          | Token               | Mean       | Error   | StdDev  | Op/s        | Ratio | Gen0   | Allocated | Alloc Ratio |
|---------------- |-------------------- |-----------:|--------:|--------:|------------:|------:|-------:|----------:|------------:|
| **SyncCompletion**  | **None**                |   **720.4 ns** | **8.87 ns** | **3.94 ns** | **1,388,069.6** |  **1.00** | **0.0305** |     **512 B** |        **1.00** |
| AsyncCompletion | None                | 1,178.8 ns | 5.67 ns | 2.02 ns |   848,340.9 |  1.64 | 0.0420 |     712 B |        1.39 |
|                 |                     |            |         |         |             |       |        |           |             |
| **SyncCompletion**  | **Live**                |   **758.5 ns** | **3.03 ns** | **1.34 ns** | **1,318,366.8** |  **1.00** | **0.0315** |     **528 B** |        **1.00** |
| AsyncCompletion | Live                | 1,233.8 ns | 8.51 ns | 4.45 ns |   810,493.3 |  1.63 | 0.0420 |     728 B |        1.38 |
|                 |                     |            |         |         |             |       |        |           |             |
| **SyncCompletion**  | **CanceledCooperative** |   **741.6 ns** | **2.62 ns** | **1.16 ns** | **1,348,479.8** |  **1.00** | **0.0315** |     **528 B** |        **1.00** |
| AsyncCompletion | CanceledCooperative | 1,225.8 ns | 8.94 ns | 3.97 ns |   815,788.7 |  1.65 | 0.0420 |     728 B |        1.38 |
