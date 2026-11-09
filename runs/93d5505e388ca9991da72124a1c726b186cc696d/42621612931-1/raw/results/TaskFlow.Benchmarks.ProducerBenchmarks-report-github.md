```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 2.60GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method                   | Producers | Mean     | Error    | StdDev   | Op/s        | Gen0   | Gen1   | Allocated |
|------------------------- |---------- |---------:|---------:|---------:|------------:|-------:|-------:|----------:|
| **ConcurrentSubmitAndDrain** | **1**         | **427.1 ns** | **23.18 ns** | **10.29 ns** | **2,341,119.5** | **0.0371** | **0.0176** |     **625 B** |
| **ConcurrentSubmitAndDrain** | **4**         | **625.2 ns** | **33.26 ns** | **17.39 ns** | **1,599,561.0** | **0.0391** | **0.0234** |     **693 B** |
| **ConcurrentSubmitAndDrain** | **16**        | **643.3 ns** | **52.83 ns** | **27.63 ns** | **1,554,452.4** | **0.0391** | **0.0234** |     **702 B** |
