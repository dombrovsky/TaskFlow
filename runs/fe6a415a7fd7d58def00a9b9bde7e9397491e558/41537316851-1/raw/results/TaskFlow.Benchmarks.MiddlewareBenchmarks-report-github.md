```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 2.59GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method             | Layers | Mean       | Error     | StdDev   | Op/s        | Gen0   | Allocated |
|------------------- |------- |-----------:|----------:|---------:|------------:|-------:|----------:|
| **PipelineCompletion** | **0**      |   **925.2 ns** |  **13.23 ns** |  **6.92 ns** | **1,080,812.4** | **0.0305** |     **512 B** |
| **PipelineCompletion** | **1**      | **2,012.6 ns** |  **49.01 ns** | **25.63 ns** |   **496,858.1** | **0.0954** |    **1607 B** |
| **PipelineCompletion** | **2**      | **2,333.9 ns** | **164.08 ns** | **72.85 ns** |   **428,463.4** | **0.1221** |    **2112 B** |
| **PipelineCompletion** | **4**      | **2,764.8 ns** |  **76.69 ns** | **34.05 ns** |   **361,695.6** | **0.1831** |    **3120 B** |
| **PipelineCompletion** | **8**      | **4,027.8 ns** |  **49.30 ns** | **21.89 ns** |   **248,274.9** | **0.3052** |    **5136 B** |
