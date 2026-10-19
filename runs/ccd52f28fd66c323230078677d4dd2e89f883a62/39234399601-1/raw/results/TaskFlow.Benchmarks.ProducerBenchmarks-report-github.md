```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V45 4.17GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method                   | Producers | Mean     | Error    | StdDev   | Op/s        | Gen0   | Gen1   | Allocated |
|------------------------- |---------- |---------:|---------:|---------:|------------:|-------:|-------:|----------:|
| **ConcurrentSubmitAndDrain** | **1**         | **339.2 ns** | **34.78 ns** | **18.19 ns** | **2,948,088.2** | **0.0532** | **0.0259** |     **880 B** |
| **ConcurrentSubmitAndDrain** | **4**         | **434.7 ns** | **23.66 ns** | **12.37 ns** | **2,300,482.7** | **0.0547** | **0.0391** |     **928 B** |
| **ConcurrentSubmitAndDrain** | **16**        | **448.9 ns** | **38.80 ns** | **20.29 ns** | **2,227,751.7** | **0.0547** | **0.0410** |     **933 B** |
