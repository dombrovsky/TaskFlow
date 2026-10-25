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
| **SyncCompletion**  | **None**                |   **567.0 ns** | **12.16 ns** |  **6.36 ns** | **1,763,520.2** |  **1.00** |    **0.01** | **0.0305** |     **512 B** |        **1.00** |
| AsyncCompletion | None                |   985.3 ns | 34.01 ns | 15.10 ns | 1,014,925.4 |  1.74 |    0.03 | 0.0420 |     712 B |        1.39 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **Live**                |   **629.5 ns** | **30.66 ns** | **16.04 ns** | **1,588,475.2** |  **1.00** |    **0.03** | **0.0315** |     **528 B** |        **1.00** |
| AsyncCompletion | Live                | 1,023.8 ns |  8.99 ns |  3.99 ns |   976,751.8 |  1.63 |    0.04 | 0.0420 |     728 B |        1.38 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **CanceledCooperative** |   **622.5 ns** | **40.30 ns** | **21.08 ns** | **1,606,448.4** |  **1.00** |    **0.04** | **0.0315** |     **528 B** |        **1.00** |
| AsyncCompletion | CanceledCooperative | 1,011.5 ns | 25.45 ns | 13.31 ns |   988,620.7 |  1.63 |    0.05 | 0.0420 |     728 B |        1.38 |
