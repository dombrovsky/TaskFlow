```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 3.24GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method                   | Producers | Mean     | Error    | StdDev   | Op/s        | Gen0   | Gen1   | Allocated |
|------------------------- |---------- |---------:|---------:|---------:|------------:|-------:|-------:|----------:|
| **ConcurrentSubmitAndDrain** | **1**         | **463.0 ns** | **38.57 ns** | **20.17 ns** | **2,159,864.1** | **0.0371** | **0.0137** |     **638 B** |
| **ConcurrentSubmitAndDrain** | **4**         | **656.7 ns** | **59.92 ns** | **31.34 ns** | **1,522,769.5** | **0.0391** | **0.0273** |     **684 B** |
| **ConcurrentSubmitAndDrain** | **16**        | **664.8 ns** | **31.26 ns** | **13.88 ns** | **1,504,190.9** | **0.0391** | **0.0234** |     **698 B** |
