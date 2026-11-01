```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 3.05GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method             | Layers | Mean       | Error    | StdDev   | Op/s        | Gen0   | Allocated |
|------------------- |------- |-----------:|---------:|---------:|------------:|-------:|----------:|
| **PipelineCompletion** | **0**      |   **998.1 ns** | **17.39 ns** |  **7.72 ns** | **1,001,854.6** | **0.0305** |     **538 B** |
| **PipelineCompletion** | **1**      | **1,974.5 ns** | **33.55 ns** | **17.55 ns** |   **506,453.9** | **0.0916** |    **1605 B** |
| **PipelineCompletion** | **2**      | **2,283.7 ns** | **65.74 ns** | **29.19 ns** |   **437,877.2** | **0.1221** |    **2112 B** |
| **PipelineCompletion** | **4**      | **2,787.5 ns** | **82.16 ns** | **42.97 ns** |   **358,742.6** | **0.1831** |    **3120 B** |
| **PipelineCompletion** | **8**      | **4,069.8 ns** | **35.88 ns** | **18.76 ns** |   **245,710.9** | **0.3052** |    **5136 B** |
