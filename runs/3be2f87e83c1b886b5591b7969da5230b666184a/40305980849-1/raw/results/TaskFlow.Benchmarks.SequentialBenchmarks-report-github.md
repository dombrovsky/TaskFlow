```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 3.04GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method          | Token               | Mean       | Error    | StdDev   | Op/s        | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------- |-------------------- |-----------:|---------:|---------:|------------:|------:|--------:|-------:|----------:|------------:|
| **SyncCompletion**  | **None**                |   **924.2 ns** | **14.40 ns** |  **7.53 ns** | **1,082,016.6** |  **1.00** |    **0.01** | **0.0305** |     **512 B** |        **1.00** |
| AsyncCompletion | None                | 1,535.6 ns | 51.63 ns | 27.00 ns |   651,231.5 |  1.66 |    0.03 | 0.0381 |     712 B |        1.39 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **Live**                |   **969.2 ns** |  **8.20 ns** |  **3.64 ns** | **1,031,727.4** |  **1.00** |    **0.00** | **0.0305** |     **528 B** |        **1.00** |
| AsyncCompletion | Live                | 1,668.1 ns |  9.62 ns |  5.03 ns |   599,497.8 |  1.72 |    0.01 | 0.0420 |     728 B |        1.38 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **CanceledCooperative** |   **945.0 ns** | **28.04 ns** | **14.67 ns** | **1,058,184.3** |  **1.00** |    **0.02** | **0.0305** |     **528 B** |        **1.00** |
| AsyncCompletion | CanceledCooperative | 1,575.7 ns | 51.91 ns | 23.05 ns |   634,641.3 |  1.67 |    0.03 | 0.0381 |     728 B |        1.38 |
