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
| **SyncCompletion**  | **None**                |   **825.0 ns** | **28.68 ns** | **12.73 ns** | **1,212,084.1** |  **1.00** |    **0.02** | **0.0305** |     **539 B** |        **1.00** |
| AsyncCompletion | None                | 1,289.9 ns | 18.18 ns |  9.51 ns |   775,238.5 |  1.56 |    0.02 | 0.0420 |     723 B |        1.34 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **Live**                |   **829.9 ns** | **11.54 ns** |  **6.04 ns** | **1,205,028.7** |  **1.00** |    **0.01** | **0.0324** |     **556 B** |        **1.00** |
| AsyncCompletion | Live                | 1,296.7 ns |  5.49 ns |  2.44 ns |   771,194.5 |  1.56 |    0.01 | 0.0439 |     742 B |        1.33 |
|                 |                     |            |          |          |             |       |         |        |           |             |
| **SyncCompletion**  | **CanceledCooperative** |   **848.6 ns** |  **4.87 ns** |  **2.16 ns** | **1,178,383.9** |  **1.00** |    **0.00** | **0.0324** |     **551 B** |        **1.00** |
| AsyncCompletion | CanceledCooperative | 1,348.0 ns | 11.09 ns |  5.80 ns |   741,813.2 |  1.59 |    0.01 | 0.0420 |     731 B |        1.33 |
