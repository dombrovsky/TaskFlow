```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
INTEL XEON PLATINUM 8573C 3.24GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method                   | Producers | Mean     | Error     | StdDev   | Op/s        | Gen0   | Gen1   | Allocated |
|------------------------- |---------- |---------:|----------:|---------:|------------:|-------:|-------:|----------:|
| **ConcurrentSubmitAndDrain** | **1**         | **408.8 ns** |  **25.32 ns** | **13.24 ns** | **2,446,482.8** | **0.0059** |      **-** |     **605 B** |
| **ConcurrentSubmitAndDrain** | **4**         | **659.7 ns** | **124.65 ns** | **65.19 ns** | **1,515,792.8** | **0.0078** |      **-** |     **689 B** |
| **ConcurrentSubmitAndDrain** | **16**        | **658.6 ns** |  **95.56 ns** | **42.43 ns** | **1,518,270.0** | **0.0078** | **0.0039** |     **699 B** |
