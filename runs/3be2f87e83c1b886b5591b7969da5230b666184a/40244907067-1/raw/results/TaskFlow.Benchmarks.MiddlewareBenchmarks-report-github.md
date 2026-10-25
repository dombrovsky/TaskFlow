```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method             | Layers | Mean       | Error     | StdDev   | Op/s        | Gen0   | Allocated |
|------------------- |------- |-----------:|----------:|---------:|------------:|-------:|----------:|
| **PipelineCompletion** | **0**      |   **985.2 ns** |  **44.42 ns** | **23.23 ns** | **1,015,061.8** | **0.0305** |     **512 B** |
| **PipelineCompletion** | **1**      | **2,255.2 ns** |  **16.96 ns** |  **7.53 ns** |   **443,412.5** | **0.0992** |    **1727 B** |
| **PipelineCompletion** | **2**      | **2,743.6 ns** |  **16.61 ns** |  **7.37 ns** |   **364,483.6** | **0.1373** |    **2352 B** |
| **PipelineCompletion** | **4**      | **3,797.6 ns** |  **67.22 ns** | **29.85 ns** |   **263,326.9** | **0.2136** |    **3600 B** |
| **PipelineCompletion** | **8**      | **5,816.8 ns** | **120.62 ns** | **63.09 ns** |   **171,914.7** | **0.3586** |    **6096 B** |
