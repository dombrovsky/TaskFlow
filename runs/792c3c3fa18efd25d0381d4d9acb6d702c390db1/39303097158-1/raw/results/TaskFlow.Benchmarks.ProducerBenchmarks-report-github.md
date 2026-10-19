```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 2.60GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method                   | Producers | Mean     | Error    | StdDev   | Op/s        | Gen0   | Gen1   | Allocated |
|------------------------- |---------- |---------:|---------:|---------:|------------:|-------:|-------:|----------:|
| **ConcurrentSubmitAndDrain** | **1**         | **434.5 ns** | **16.75 ns** |  **7.44 ns** | **2,301,424.7** | **0.0371** | **0.0195** |     **639 B** |
| **ConcurrentSubmitAndDrain** | **4**         | **624.6 ns** | **27.93 ns** | **12.40 ns** | **1,600,963.6** | **0.0391** | **0.0234** |     **700 B** |
| **ConcurrentSubmitAndDrain** | **16**        | **618.1 ns** | **18.56 ns** |  **9.71 ns** | **1,617,800.8** | **0.0420** | **0.0293** |     **702 B** |
