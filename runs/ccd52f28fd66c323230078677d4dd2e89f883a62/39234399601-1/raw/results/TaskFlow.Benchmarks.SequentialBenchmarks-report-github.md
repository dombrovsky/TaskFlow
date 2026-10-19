```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V45 4.17GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method          | Token               | Mean       | Error    | StdDev   | Op/s        | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------- |-------------------- |-----------:|---------:|---------:|------------:|------:|--------:|-------:|----------:|------------:|
| **SyncCompletion**  | **None**                |   **689.9 ns** | **59.83 ns** | **26.57 ns** | **1,449,439.0** |  **1.00** |    **0.05** | **0.0439** |     **736 B** |        **1.00** |
| AsyncCompletion | None                | 1,096.2 ns | 97.65 ns | 51.08 ns |   912,251.3 |  1.59 |    0.09 | 0.0534 |     944 B |        1.28 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **Live**                |   **693.8 ns** | **31.31 ns** | **13.90 ns** | **1,441,360.8** |  **1.00** |    **0.03** | **0.0439** |     **752 B** |        **1.00** |
| AsyncCompletion | Live                | 1,138.7 ns | 24.41 ns | 10.84 ns |   878,209.4 |  1.64 |    0.03 | 0.0572 |     960 B |        1.28 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **CanceledCooperative** |   **782.9 ns** | **34.43 ns** | **15.29 ns** | **1,277,343.9** |  **1.00** |    **0.03** | **0.0448** |     **752 B** |        **1.00** |
| AsyncCompletion | CanceledCooperative | 1,104.9 ns | 28.32 ns | 14.81 ns |   905,048.2 |  1.41 |    0.03 | 0.0572 |     960 B |        1.28 |
