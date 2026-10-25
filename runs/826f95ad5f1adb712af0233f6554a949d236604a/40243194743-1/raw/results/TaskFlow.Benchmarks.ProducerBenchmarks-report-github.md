```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V74 2.60GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method                   | Producers | Mean     | Error    | StdDev   | Op/s        | Gen0   | Gen1   | Allocated |
|------------------------- |---------- |---------:|---------:|---------:|------------:|-------:|-------:|----------:|
| **ConcurrentSubmitAndDrain** | **1**         | **317.1 ns** | **29.22 ns** | **15.28 ns** | **3,153,096.5** | **0.0371** | **0.0137** |     **619 B** |
| **ConcurrentSubmitAndDrain** | **4**         | **467.5 ns** | **47.82 ns** | **25.01 ns** | **2,139,123.9** | **0.0410** | **0.0234** |     **698 B** |
| **ConcurrentSubmitAndDrain** | **16**        | **467.9 ns** | **34.09 ns** | **15.14 ns** | **2,137,144.8** | **0.0410** | **0.0273** |     **702 B** |
