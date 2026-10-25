```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V74 3.69GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method                   | Producers | Mean     | Error    | StdDev   | Op/s        | Gen0   | Gen1   | Allocated |
|------------------------- |---------- |---------:|---------:|---------:|------------:|-------:|-------:|----------:|
| **ConcurrentSubmitAndDrain** | **1**         | **287.3 ns** | **35.91 ns** | **18.78 ns** | **3,480,379.8** | **0.0371** | **0.0156** |     **612 B** |
| **ConcurrentSubmitAndDrain** | **4**         | **420.2 ns** | **28.20 ns** | **12.52 ns** | **2,379,848.6** | **0.0410** | **0.0234** |     **699 B** |
| **ConcurrentSubmitAndDrain** | **16**        | **447.8 ns** | **40.77 ns** | **18.10 ns** | **2,233,003.1** | **0.0410** | **0.0273** |     **699 B** |
