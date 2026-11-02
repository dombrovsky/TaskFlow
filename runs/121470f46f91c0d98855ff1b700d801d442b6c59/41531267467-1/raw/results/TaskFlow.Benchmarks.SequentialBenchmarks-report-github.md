```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 3.24GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method          | Token               | Mean       | Error    | StdDev   | Op/s        | Ratio | Gen0   | Allocated | Alloc Ratio |
|---------------- |-------------------- |-----------:|---------:|---------:|------------:|------:|-------:|----------:|------------:|
| **SyncCompletion**  | **None**                |   **940.7 ns** |  **6.22 ns** |  **2.76 ns** | **1,063,071.7** |  **1.00** | **0.0305** |     **512 B** |        **1.00** |
| AsyncCompletion | None                | 1,589.5 ns | 23.49 ns | 10.43 ns |   629,116.1 |  1.69 | 0.0381 |     712 B |        1.39 |
|                 |                     |            |          |          |             |       |        |           |             |
| **SyncCompletion**  | **Live**                |   **962.1 ns** |  **2.69 ns** |  **0.96 ns** | **1,039,352.9** |  **1.00** | **0.0305** |     **528 B** |        **1.00** |
| AsyncCompletion | Live                | 1,680.6 ns | 11.64 ns |  4.15 ns |   595,022.2 |  1.75 | 0.0381 |     728 B |        1.38 |
|                 |                     |            |          |          |             |       |        |           |             |
| **SyncCompletion**  | **CanceledCooperative** |   **923.6 ns** | **15.62 ns** |  **6.93 ns** | **1,082,725.8** |  **1.00** | **0.0305** |     **528 B** |        **1.00** |
| AsyncCompletion | CanceledCooperative | 1,596.7 ns | 13.53 ns |  6.01 ns |   626,307.5 |  1.73 | 0.0381 |     728 B |        1.38 |
