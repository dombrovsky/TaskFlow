```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method                   | Producers | Mean     | Error    | StdDev   | Op/s        | Gen0   | Gen1   | Allocated |
|------------------------- |---------- |---------:|---------:|---------:|------------:|-------:|-------:|----------:|
| **ConcurrentSubmitAndDrain** | **1**         | **422.6 ns** | **22.83 ns** | **10.14 ns** | **2,366,468.0** | **0.0371** | **0.0176** |     **621 B** |
| **ConcurrentSubmitAndDrain** | **4**         | **612.3 ns** | **23.91 ns** | **12.50 ns** | **1,633,094.3** | **0.0391** | **0.0234** |     **691 B** |
| **ConcurrentSubmitAndDrain** | **16**        | **652.1 ns** | **58.49 ns** | **30.59 ns** | **1,533,610.5** | **0.0391** | **0.0234** |     **698 B** |
