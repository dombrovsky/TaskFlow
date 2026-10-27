```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
INTEL XEON PLATINUM 8573C 3.00GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method             | Layers | Mean     | Error     | StdDev    | Op/s      | Gen0   | Allocated |
|------------------- |------- |---------:|----------:|----------:|----------:|-------:|----------:|
| **PipelineCompletion** | **0**      | **1.274 μs** | **0.0441 μs** | **0.0231 μs** | **785,003.5** | **0.0038** |     **513 B** |
| **PipelineCompletion** | **1**      | **2.461 μs** | **0.0477 μs** | **0.0212 μs** | **406,318.4** | **0.0153** |    **1605 B** |
| **PipelineCompletion** | **2**      | **2.655 μs** | **0.0227 μs** | **0.0119 μs** | **376,585.6** | **0.0229** |    **2110 B** |
| **PipelineCompletion** | **4**      | **3.318 μs** | **0.0460 μs** | **0.0241 μs** | **301,377.0** | **0.0305** |    **3120 B** |
| **PipelineCompletion** | **8**      | **4.449 μs** | **0.0775 μs** | **0.0406 μs** | **224,772.8** | **0.0610** |    **5136 B** |
