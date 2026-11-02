```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 2.63GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method             | Layers | Mean       | Error     | StdDev   | Op/s        | Gen0   | Allocated |
|------------------- |------- |-----------:|----------:|---------:|------------:|-------:|----------:|
| **PipelineCompletion** | **0**      |   **923.0 ns** |  **26.67 ns** | **11.84 ns** | **1,083,405.3** | **0.0305** |     **512 B** |
| **PipelineCompletion** | **1**      | **1,996.0 ns** |  **55.58 ns** | **24.68 ns** |   **501,012.7** | **0.0954** |    **1607 B** |
| **PipelineCompletion** | **2**      | **2,320.2 ns** | **132.16 ns** | **69.12 ns** |   **431,000.6** | **0.1221** |    **2111 B** |
| **PipelineCompletion** | **4**      | **2,796.6 ns** |  **75.94 ns** | **33.72 ns** |   **357,581.2** | **0.1831** |    **3120 B** |
| **PipelineCompletion** | **8**      | **4,034.2 ns** |  **22.26 ns** | **11.64 ns** |   **247,883.1** | **0.3052** |    **5136 B** |
