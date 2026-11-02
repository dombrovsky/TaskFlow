```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V74 2.84GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method          | Token               | Mean       | Error    | StdDev   | Op/s        | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------- |-------------------- |-----------:|---------:|---------:|------------:|------:|--------:|-------:|----------:|------------:|
| **SyncCompletion**  | **None**                |   **965.3 ns** | **12.94 ns** |  **5.74 ns** | **1,035,929.1** |  **1.00** |    **0.01** | **0.0305** |     **512 B** |        **1.00** |
| AsyncCompletion | None                | 1,580.9 ns | 19.88 ns | 10.40 ns |   632,562.4 |  1.64 |    0.01 | 0.0420 |     712 B |        1.39 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **Live**                | **1,034.6 ns** | **10.20 ns** |  **4.53 ns** |   **966,560.5** |  **1.00** |    **0.01** | **0.0305** |     **528 B** |        **1.00** |
| AsyncCompletion | Live                | 1,633.4 ns | 16.22 ns |  7.20 ns |   612,223.6 |  1.58 |    0.01 | 0.0420 |     728 B |        1.38 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **CanceledCooperative** | **1,044.0 ns** | **31.20 ns** | **13.85 ns** |   **957,821.9** |  **1.00** |    **0.02** | **0.0305** |     **528 B** |        **1.00** |
| AsyncCompletion | CanceledCooperative | 1,602.8 ns | 59.98 ns | 31.37 ns |   623,927.6 |  1.54 |    0.03 | 0.0381 |     728 B |        1.38 |
