```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V45 2.60GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method          | Token               | Mean       | Error    | StdDev   | Op/s        | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------- |-------------------- |-----------:|---------:|---------:|------------:|------:|--------:|-------:|----------:|------------:|
| **SyncCompletion**  | **None**                |   **693.9 ns** | **24.98 ns** | **13.06 ns** | **1,441,068.0** |  **1.00** |    **0.03** | **0.0439** |     **736 B** |        **1.00** |
| AsyncCompletion | None                | 1,085.2 ns | 45.31 ns | 23.70 ns |   921,501.7 |  1.56 |    0.04 | 0.0553 |     944 B |        1.28 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **Live**                |   **764.4 ns** | **42.03 ns** | **21.98 ns** | **1,308,167.4** |  **1.00** |    **0.04** | **0.0448** |     **752 B** |        **1.00** |
| AsyncCompletion | Live                | 1,086.4 ns | 10.41 ns |  4.62 ns |   920,467.9 |  1.42 |    0.04 | 0.0572 |     960 B |        1.28 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **CanceledCooperative** |   **675.1 ns** | **21.47 ns** |  **9.53 ns** | **1,481,211.8** |  **1.00** |    **0.02** | **0.0439** |     **752 B** |        **1.00** |
| AsyncCompletion | CanceledCooperative | 1,071.5 ns | 22.84 ns | 11.95 ns |   933,230.2 |  1.59 |    0.03 | 0.0572 |     960 B |        1.28 |
