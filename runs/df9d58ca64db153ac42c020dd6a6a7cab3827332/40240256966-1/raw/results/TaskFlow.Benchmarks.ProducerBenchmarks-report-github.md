```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V45 2.60GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method                   | Producers | Mean     | Error    | StdDev   | Op/s        | Gen0   | Gen1   | Allocated |
|------------------------- |---------- |---------:|---------:|---------:|------------:|-------:|-------:|----------:|
| **ConcurrentSubmitAndDrain** | **1**         | **246.9 ns** | **17.90 ns** |  **9.36 ns** | **4,050,350.3** | **0.0371** | **0.0166** |     **618 B** |
| **ConcurrentSubmitAndDrain** | **4**         | **389.1 ns** | **58.26 ns** | **30.47 ns** | **2,570,315.1** | **0.0410** | **0.0273** |     **695 B** |
| **ConcurrentSubmitAndDrain** | **16**        | **404.8 ns** | **27.45 ns** | **14.36 ns** | **2,470,467.9** | **0.0420** | **0.0283** |     **700 B** |
