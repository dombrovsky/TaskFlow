```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 2.63GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method                   | Producers | Mean     | Error    | StdDev   | Op/s        | Gen0   | Gen1   | Allocated |
|------------------------- |---------- |---------:|---------:|---------:|------------:|-------:|-------:|----------:|
| **ConcurrentSubmitAndDrain** | **1**         | **465.8 ns** | **35.80 ns** | **18.72 ns** | **2,146,670.7** | **0.0391** | **0.0137** |     **651 B** |
| **ConcurrentSubmitAndDrain** | **4**         | **640.3 ns** | **48.85 ns** | **25.55 ns** | **1,561,704.1** | **0.0391** | **0.0234** |     **702 B** |
| **ConcurrentSubmitAndDrain** | **16**        | **631.1 ns** | **20.52 ns** | **10.73 ns** | **1,584,414.1** | **0.0391** | **0.0273** |     **699 B** |
