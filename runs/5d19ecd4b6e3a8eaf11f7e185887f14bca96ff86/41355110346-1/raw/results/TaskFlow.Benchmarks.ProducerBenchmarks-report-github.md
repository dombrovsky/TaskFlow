```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 3.05GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method                   | Producers | Mean     | Error    | StdDev   | Op/s        | Gen0   | Gen1   | Allocated |
|------------------------- |---------- |---------:|---------:|---------:|------------:|-------:|-------:|----------:|
| **ConcurrentSubmitAndDrain** | **1**         | **577.0 ns** | **17.10 ns** |  **7.59 ns** | **1,733,213.2** | **0.0381** | **0.0176** |     **634 B** |
| **ConcurrentSubmitAndDrain** | **4**         | **910.4 ns** | **27.53 ns** | **12.22 ns** | **1,098,385.1** | **0.0391** | **0.0234** |     **661 B** |
| **ConcurrentSubmitAndDrain** | **16**        | **909.9 ns** | **10.06 ns** |  **5.26 ns** | **1,098,972.0** | **0.0391** | **0.0273** |     **665 B** |
