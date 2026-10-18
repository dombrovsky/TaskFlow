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
| **ConcurrentSubmitAndDrain** | **1**         | **322.6 ns** | **52.77 ns** | **27.60 ns** | **3,100,074.3** | **0.0508** | **0.0215** |     **863 B** |
| **ConcurrentSubmitAndDrain** | **4**         | **408.8 ns** | **21.95 ns** |  **9.75 ns** | **2,446,146.0** | **0.0562** | **0.0430** |     **931 B** |
| **ConcurrentSubmitAndDrain** | **16**        | **443.7 ns** | **61.89 ns** | **32.37 ns** | **2,253,547.7** | **0.0557** | **0.0371** |     **938 B** |
