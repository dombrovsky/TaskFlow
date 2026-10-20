```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V74 2.86GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method                   | Producers | Mean     | Error    | StdDev   | Op/s        | Gen0   | Gen1   | Allocated |
|------------------------- |---------- |---------:|---------:|---------:|------------:|-------:|-------:|----------:|
| **ConcurrentSubmitAndDrain** | **1**         | **376.9 ns** | **27.58 ns** | **14.42 ns** | **2,653,190.4** | **0.0391** | **0.0137** |     **653 B** |
| **ConcurrentSubmitAndDrain** | **4**         | **587.4 ns** | **90.73 ns** | **47.45 ns** | **1,702,398.5** | **0.0391** | **0.0195** |     **689 B** |
| **ConcurrentSubmitAndDrain** | **16**        | **577.9 ns** | **51.64 ns** | **22.93 ns** | **1,730,472.0** | **0.0410** | **0.0293** |     **698 B** |
