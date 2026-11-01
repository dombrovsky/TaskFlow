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
| **SyncCompletion**  | **None**                |   **862.0 ns** | **44.65 ns** | **23.35 ns** | **1,160,051.2** |  **1.00** |    **0.04** | **0.0315** |     **539 B** |        **1.00** |
| AsyncCompletion | None                | 1,243.9 ns |  6.95 ns |  3.64 ns |   803,911.5 |  1.44 |    0.04 | 0.0420 |     724 B |        1.34 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **Live**                |   **837.3 ns** |  **8.94 ns** |  **4.67 ns** | **1,194,318.6** |  **1.00** |    **0.01** | **0.0324** |     **554 B** |        **1.00** |
| AsyncCompletion | Live                | 1,305.8 ns | 16.48 ns |  7.32 ns |   765,823.4 |  1.56 |    0.01 | 0.0420 |     740 B |        1.34 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **CanceledCooperative** |   **910.9 ns** | **32.20 ns** | **16.84 ns** | **1,097,820.9** |  **1.00** |    **0.02** | **0.0324** |     **548 B** |        **1.00** |
| AsyncCompletion | CanceledCooperative | 1,355.2 ns | 21.89 ns |  9.72 ns |   737,920.5 |  1.49 |    0.03 | 0.0420 |     736 B |        1.34 |
