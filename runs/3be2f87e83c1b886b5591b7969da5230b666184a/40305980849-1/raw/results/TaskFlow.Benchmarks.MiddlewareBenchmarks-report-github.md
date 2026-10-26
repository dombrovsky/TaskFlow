```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 3.04GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method             | Layers | Mean     | Error     | StdDev    | Op/s      | Gen0   | Allocated |
|------------------- |------- |---------:|----------:|----------:|----------:|-------:|----------:|
| **PipelineCompletion** | **0**      | **1.002 μs** | **0.0365 μs** | **0.0191 μs** | **998,108.3** | **0.0305** |     **512 B** |
| **PipelineCompletion** | **1**      | **2.292 μs** | **0.0297 μs** | **0.0155 μs** | **436,308.2** | **0.1030** |    **1727 B** |
| **PipelineCompletion** | **2**      | **2.856 μs** | **0.0163 μs** | **0.0073 μs** | **350,107.8** | **0.1373** |    **2352 B** |
| **PipelineCompletion** | **4**      | **3.796 μs** | **0.0546 μs** | **0.0242 μs** | **263,451.0** | **0.2136** |    **3600 B** |
| **PipelineCompletion** | **8**      | **5.876 μs** | **0.2277 μs** | **0.1191 μs** | **170,185.2** | **0.3357** |    **6096 B** |
