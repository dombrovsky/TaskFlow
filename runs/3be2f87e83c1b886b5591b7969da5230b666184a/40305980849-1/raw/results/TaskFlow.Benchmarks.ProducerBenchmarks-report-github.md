```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 3.04GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method                   | Producers | Mean     | Error    | StdDev   | Op/s        | Gen0   | Gen1   | Allocated |
|------------------------- |---------- |---------:|---------:|---------:|------------:|-------:|-------:|----------:|
| **ConcurrentSubmitAndDrain** | **1**         | **479.8 ns** | **48.43 ns** | **21.50 ns** | **2,084,213.2** | **0.0352** | **0.0117** |     **638 B** |
| **ConcurrentSubmitAndDrain** | **4**         | **610.9 ns** | **18.09 ns** |  **6.45 ns** | **1,636,901.5** | **0.0391** | **0.0234** |     **696 B** |
| **ConcurrentSubmitAndDrain** | **16**        | **669.3 ns** | **70.55 ns** | **36.90 ns** | **1,494,009.3** | **0.0391** | **0.0273** |     **696 B** |
