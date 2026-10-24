```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
INTEL XEON PLATINUM 8573C 3.24GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method          | Token               | Mean     | Error     | StdDev    | Op/s      | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------- |-------------------- |---------:|----------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| **SyncCompletion**  | **None**                | **1.158 μs** | **0.0082 μs** | **0.0043 μs** | **863,792.5** |  **1.00** |    **0.00** | **0.0057** |     **513 B** |        **1.00** |
| AsyncCompletion | None                | 2.141 μs | 0.0337 μs | 0.0150 μs | 467,049.3 |  1.85 |    0.01 | 0.0076 |     712 B |        1.39 |
|                 |                     |          |           |           |           |       |         |        |           |             |
| **SyncCompletion**  | **Live**                | **1.280 μs** | **0.0267 μs** | **0.0140 μs** | **781,416.8** |  **1.00** |    **0.01** | **0.0057** |     **529 B** |        **1.00** |
| AsyncCompletion | Live                | 2.127 μs | 0.0562 μs | 0.0294 μs | 470,179.2 |  1.66 |    0.03 | 0.0076 |     728 B |        1.38 |
|                 |                     |          |           |           |           |       |         |        |           |             |
| **SyncCompletion**  | **CanceledCooperative** | **1.195 μs** | **0.0383 μs** | **0.0200 μs** | **837,092.5** |  **1.00** |    **0.02** | **0.0057** |     **529 B** |        **1.00** |
| AsyncCompletion | CanceledCooperative | 2.226 μs | 0.0790 μs | 0.0413 μs | 449,143.5 |  1.86 |    0.04 | 0.0076 |     728 B |        1.38 |
