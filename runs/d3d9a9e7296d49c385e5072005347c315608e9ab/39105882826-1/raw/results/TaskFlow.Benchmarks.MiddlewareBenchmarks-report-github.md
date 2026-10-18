```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method             | Layers | Mean     | Error     | StdDev    | Op/s      | Gen0   | Allocated |
|------------------- |------- |---------:|----------:|----------:|----------:|-------:|----------:|
| **PipelineCompletion** | **0**      | **1.098 μs** | **0.0117 μs** | **0.0052 μs** | **910,598.5** | **0.0439** |     **736 B** |
| **PipelineCompletion** | **1**      | **3.189 μs** | **0.0561 μs** | **0.0293 μs** | **313,570.8** | **0.1526** |    **2751 B** |
| **PipelineCompletion** | **2**      | **3.624 μs** | **0.0244 μs** | **0.0108 μs** | **275,911.8** | **0.1984** |    **3360 B** |
| **PipelineCompletion** | **4**      | **4.750 μs** | **0.5521 μs** | **0.2888 μs** | **210,539.3** | **0.2441** |    **4575 B** |
| **PipelineCompletion** | **8**      | **6.267 μs** | **0.3409 μs** | **0.1783 μs** | **159,567.0** | **0.3967** |    **7008 B** |
