```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 2.66GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method                   | Producers | Mean     | Error    | StdDev   | Op/s        | Gen0   | Gen1   | Allocated |
|------------------------- |---------- |---------:|---------:|---------:|------------:|-------:|-------:|----------:|
| **ConcurrentSubmitAndDrain** | **1**         | **451.0 ns** |  **6.94 ns** |  **3.63 ns** | **2,217,310.6** | **0.0371** | **0.0195** |     **641 B** |
| **ConcurrentSubmitAndDrain** | **4**         | **642.5 ns** | **33.96 ns** | **17.76 ns** | **1,556,451.7** | **0.0391** | **0.0234** |     **699 B** |
| **ConcurrentSubmitAndDrain** | **16**        | **645.8 ns** | **55.55 ns** | **29.05 ns** | **1,548,423.0** | **0.0391** | **0.0234** |     **702 B** |
