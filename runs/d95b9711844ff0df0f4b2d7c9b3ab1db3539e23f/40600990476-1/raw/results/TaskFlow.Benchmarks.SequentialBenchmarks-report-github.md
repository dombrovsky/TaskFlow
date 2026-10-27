```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
INTEL XEON PLATINUM 8573C 3.00GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method          | Token               | Mean     | Error     | StdDev    | Op/s      | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------- |-------------------- |---------:|----------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| **SyncCompletion**  | **None**                | **1.199 μs** | **0.0282 μs** | **0.0147 μs** | **834,168.0** |  **1.00** |    **0.02** | **0.0057** |     **513 B** |        **1.00** |
| AsyncCompletion | None                | 2.214 μs | 0.0700 μs | 0.0366 μs | 451,654.2 |  1.85 |    0.04 | 0.0076 |     712 B |        1.39 |
|                 |                     |          |           |           |           |       |         |        |           |             |
| **SyncCompletion**  | **Live**                | **1.224 μs** | **0.0113 μs** | **0.0059 μs** | **817,245.1** |  **1.00** |    **0.01** | **0.0038** |     **529 B** |        **1.00** |
| AsyncCompletion | Live                | 2.241 μs | 0.0451 μs | 0.0200 μs | 446,227.3 |  1.83 |    0.02 | 0.0076 |     728 B |        1.38 |
|                 |                     |          |           |           |           |       |         |        |           |             |
| **SyncCompletion**  | **CanceledCooperative** | **1.245 μs** | **0.0180 μs** | **0.0094 μs** | **803,349.5** |  **1.00** |    **0.01** | **0.0057** |     **529 B** |        **1.00** |
| AsyncCompletion | CanceledCooperative | 2.350 μs | 0.0308 μs | 0.0137 μs | 425,498.7 |  1.89 |    0.02 | 0.0076 |     728 B |        1.38 |
