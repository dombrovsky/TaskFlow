```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V74 3.69GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method                   | Producers | Mean     | Error    | StdDev   | Op/s        | Gen0   | Gen1   | Allocated |
|------------------------- |---------- |---------:|---------:|---------:|------------:|-------:|-------:|----------:|
| **ConcurrentSubmitAndDrain** | **1**         | **408.6 ns** |  **6.60 ns** |  **2.93 ns** | **2,447,176.3** | **0.0371** | **0.0156** |     **622 B** |
| **ConcurrentSubmitAndDrain** | **4**         | **685.2 ns** | **30.13 ns** | **13.38 ns** | **1,459,460.6** | **0.0391** | **0.0234** |     **664 B** |
| **ConcurrentSubmitAndDrain** | **16**        | **728.9 ns** | **67.07 ns** | **29.78 ns** | **1,371,995.2** | **0.0391** | **0.0313** |     **664 B** |
