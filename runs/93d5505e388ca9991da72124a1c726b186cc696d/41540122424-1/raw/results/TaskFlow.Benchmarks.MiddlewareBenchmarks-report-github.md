```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 2.66GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method             | Layers | Mean       | Error    | StdDev   | Op/s        | Gen0   | Allocated |
|------------------- |------- |-----------:|---------:|---------:|------------:|-------:|----------:|
| **PipelineCompletion** | **0**      |   **996.1 ns** | **25.79 ns** | **13.49 ns** | **1,003,923.4** | **0.0305** |     **512 B** |
| **PipelineCompletion** | **1**      | **1,937.6 ns** | **44.12 ns** | **23.08 ns** |   **516,093.0** | **0.0954** |    **1607 B** |
| **PipelineCompletion** | **2**      | **2,278.8 ns** | **65.22 ns** | **34.11 ns** |   **438,819.1** | **0.1221** |    **2112 B** |
| **PipelineCompletion** | **4**      | **2,742.0 ns** | **26.98 ns** | **14.11 ns** |   **364,702.6** | **0.1869** |    **3120 B** |
| **PipelineCompletion** | **8**      | **3,953.0 ns** | **23.95 ns** | **10.63 ns** |   **252,972.7** | **0.3052** |    **5136 B** |
