```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V74 2.86GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method          | Token               | Mean       | Error    | StdDev   | Op/s        | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------- |-------------------- |-----------:|---------:|---------:|------------:|------:|--------:|-------:|----------:|------------:|
| **SyncCompletion**  | **None**                |   **965.4 ns** |  **5.21 ns** |  **2.31 ns** | **1,035,837.1** |  **1.00** |    **0.00** | **0.0305** |     **512 B** |        **1.00** |
| AsyncCompletion | None                | 1,582.8 ns | 49.54 ns | 25.91 ns |   631,803.6 |  1.64 |    0.03 | 0.0381 |     712 B |        1.39 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **Live**                | **1,072.5 ns** | **45.79 ns** | **20.33 ns** |   **932,419.3** |  **1.00** |    **0.03** | **0.0305** |     **528 B** |        **1.00** |
| AsyncCompletion | Live                | 1,642.7 ns |  8.97 ns |  4.69 ns |   608,748.0 |  1.53 |    0.03 | 0.0420 |     728 B |        1.38 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **CanceledCooperative** |   **975.8 ns** | **10.39 ns** |  **5.43 ns** | **1,024,820.0** |  **1.00** |    **0.01** | **0.0305** |     **528 B** |        **1.00** |
| AsyncCompletion | CanceledCooperative | 1,577.7 ns | 14.17 ns |  7.41 ns |   633,822.0 |  1.62 |    0.01 | 0.0420 |     728 B |        1.38 |
