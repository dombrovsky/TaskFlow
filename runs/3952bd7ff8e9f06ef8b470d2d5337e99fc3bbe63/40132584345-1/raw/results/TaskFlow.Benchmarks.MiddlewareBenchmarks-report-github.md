```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
INTEL XEON PLATINUM 8573C 3.24GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method             | Layers | Mean     | Error     | StdDev    | Op/s      | Gen0   | Allocated |
|------------------- |------- |---------:|----------:|----------:|----------:|-------:|----------:|
| **PipelineCompletion** | **0**      | **1.092 μs** | **0.0186 μs** | **0.0082 μs** | **915,794.6** | **0.0057** |     **513 B** |
| **PipelineCompletion** | **1**      | **2.740 μs** | **0.0496 μs** | **0.0260 μs** | **364,947.3** | **0.0191** |    **1790 B** |
| **PipelineCompletion** | **2**      | **3.313 μs** | **0.0742 μs** | **0.0388 μs** | **301,880.6** | **0.0267** |    **2416 B** |
| **PipelineCompletion** | **4**      | **4.250 μs** | **0.1691 μs** | **0.0885 μs** | **235,309.8** | **0.0305** |    **3664 B** |
| **PipelineCompletion** | **8**      | **6.180 μs** | **0.4322 μs** | **0.2261 μs** | **161,806.9** | **0.0687** |    **6160 B** |
