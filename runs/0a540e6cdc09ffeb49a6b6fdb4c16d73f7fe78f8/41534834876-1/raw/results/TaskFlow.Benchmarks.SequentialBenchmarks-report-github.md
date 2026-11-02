```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 2.63GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method          | Token               | Mean       | Error    | StdDev   | Op/s        | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------- |-------------------- |-----------:|---------:|---------:|------------:|------:|--------:|-------:|----------:|------------:|
| **SyncCompletion**  | **None**                |   **935.0 ns** | **41.00 ns** | **21.44 ns** | **1,069,494.7** |  **1.00** |    **0.03** | **0.0305** |     **512 B** |        **1.00** |
| AsyncCompletion | None                | 1,526.5 ns | 13.53 ns |  6.01 ns |   655,106.7 |  1.63 |    0.04 | 0.0381 |     712 B |        1.39 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **Live**                |   **934.0 ns** | **22.04 ns** |  **9.79 ns** | **1,070,697.1** |  **1.00** |    **0.01** | **0.0305** |     **528 B** |        **1.00** |
| AsyncCompletion | Live                | 1,615.1 ns | 11.25 ns |  4.99 ns |   619,165.7 |  1.73 |    0.02 | 0.0381 |     728 B |        1.38 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **CanceledCooperative** |   **924.1 ns** |  **6.75 ns** |  **3.00 ns** | **1,082,187.2** |  **1.00** |    **0.00** | **0.0305** |     **528 B** |        **1.00** |
| AsyncCompletion | CanceledCooperative | 1,580.4 ns | 15.14 ns |  6.72 ns |   632,734.2 |  1.71 |    0.01 | 0.0381 |     728 B |        1.38 |
