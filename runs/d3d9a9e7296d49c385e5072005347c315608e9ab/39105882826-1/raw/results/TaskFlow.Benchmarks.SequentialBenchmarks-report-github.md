```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method          | Token               | Mean     | Error     | StdDev    | Op/s      | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------- |-------------------- |---------:|----------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| **SyncCompletion**  | **None**                | **1.079 μs** | **0.0087 μs** | **0.0039 μs** | **926,708.9** |  **1.00** |    **0.00** | **0.0439** |     **736 B** |        **1.00** |
| AsyncCompletion | None                | 1.841 μs | 0.0645 μs | 0.0337 μs | 543,183.2 |  1.71 |    0.03 | 0.0534 |     944 B |        1.28 |
|                 |                     |          |           |           |           |       |         |        |           |             |
| **SyncCompletion**  | **Live**                | **1.121 μs** | **0.0145 μs** | **0.0076 μs** | **892,102.5** |  **1.00** |    **0.01** | **0.0439** |     **752 B** |        **1.00** |
| AsyncCompletion | Live                | 1.869 μs | 0.0253 μs | 0.0132 μs | 534,936.0 |  1.67 |    0.02 | 0.0534 |     960 B |        1.28 |
|                 |                     |          |           |           |           |       |         |        |           |             |
| **SyncCompletion**  | **CanceledCooperative** | **1.045 μs** | **0.0045 μs** | **0.0020 μs** | **956,560.2** |  **1.00** |    **0.00** | **0.0439** |     **752 B** |        **1.00** |
| AsyncCompletion | CanceledCooperative | 1.806 μs | 0.0336 μs | 0.0176 μs | 553,575.5 |  1.73 |    0.02 | 0.0534 |     960 B |        1.28 |
