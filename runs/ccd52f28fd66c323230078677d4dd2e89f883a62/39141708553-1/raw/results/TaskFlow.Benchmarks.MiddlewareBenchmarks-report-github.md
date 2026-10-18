```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V45 2.60GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method             | Layers | Mean       | Error    | StdDev   | Op/s        | Gen0   | Allocated |
|------------------- |------- |-----------:|---------:|---------:|------------:|-------:|----------:|
| **PipelineCompletion** | **0**      |   **651.0 ns** | **26.90 ns** | **14.07 ns** | **1,536,134.3** | **0.0439** |     **736 B** |
| **PipelineCompletion** | **1**      | **1,722.3 ns** | **44.59 ns** | **23.32 ns** |   **580,622.4** | **0.1602** |    **2784 B** |
| **PipelineCompletion** | **2**      | **1,971.7 ns** | **61.07 ns** | **31.94 ns** |   **507,171.4** | **0.1984** |    **3416 B** |
| **PipelineCompletion** | **4**      | **2,494.9 ns** | **55.54 ns** | **29.05 ns** |   **400,813.5** | **0.2747** |    **4680 B** |
| **PipelineCompletion** | **8**      | **3,623.6 ns** | **85.55 ns** | **37.99 ns** |   **275,971.4** | **0.4311** |    **7208 B** |
