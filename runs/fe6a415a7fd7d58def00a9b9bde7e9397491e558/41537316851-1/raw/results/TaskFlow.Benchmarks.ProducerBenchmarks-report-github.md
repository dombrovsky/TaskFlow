```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 2.59GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method                   | Producers | Mean     | Error    | StdDev   | Op/s        | Gen0   | Gen1   | Allocated |
|------------------------- |---------- |---------:|---------:|---------:|------------:|-------:|-------:|----------:|
| **ConcurrentSubmitAndDrain** | **1**         | **453.0 ns** | **13.27 ns** |  **6.94 ns** | **2,207,300.9** | **0.0386** | **0.0161** |     **642 B** |
| **ConcurrentSubmitAndDrain** | **4**         | **620.3 ns** | **24.84 ns** | **11.03 ns** | **1,611,996.8** | **0.0391** | **0.0156** |     **689 B** |
| **ConcurrentSubmitAndDrain** | **16**        | **609.6 ns** | **21.60 ns** | **11.30 ns** | **1,640,342.5** | **0.0391** | **0.0273** |     **699 B** |
