```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V74 2.84GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method                   | Producers | Mean     | Error    | StdDev   | Op/s        | Gen0   | Gen1   | Allocated |
|------------------------- |---------- |---------:|---------:|---------:|------------:|-------:|-------:|----------:|
| **ConcurrentSubmitAndDrain** | **1**         | **405.4 ns** | **92.61 ns** | **48.44 ns** | **2,466,592.1** | **0.0371** | **0.0156** |     **611 B** |
| **ConcurrentSubmitAndDrain** | **4**         | **568.5 ns** | **25.16 ns** |  **8.97 ns** | **1,759,059.1** | **0.0391** | **0.0234** |     **699 B** |
| **ConcurrentSubmitAndDrain** | **16**        | **574.4 ns** | **25.06 ns** | **13.11 ns** | **1,740,844.9** | **0.0391** | **0.0234** |     **698 B** |
