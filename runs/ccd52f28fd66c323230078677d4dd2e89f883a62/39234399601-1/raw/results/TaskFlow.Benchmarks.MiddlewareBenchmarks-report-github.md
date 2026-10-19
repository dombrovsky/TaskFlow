```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V45 4.17GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method             | Layers | Mean       | Error     | StdDev   | Op/s        | Gen0   | Allocated |
|------------------- |------- |-----------:|----------:|---------:|------------:|-------:|----------:|
| **PipelineCompletion** | **0**      |   **753.2 ns** |  **52.60 ns** | **27.51 ns** | **1,327,751.7** | **0.0439** |     **736 B** |
| **PipelineCompletion** | **1**      | **1,859.5 ns** |  **40.26 ns** | **17.88 ns** |   **537,777.7** | **0.1659** |    **2784 B** |
| **PipelineCompletion** | **2**      | **2,071.9 ns** |  **72.53 ns** | **32.20 ns** |   **482,637.5** | **0.1984** |    **3416 B** |
| **PipelineCompletion** | **4**      | **2,642.5 ns** | **118.21 ns** | **61.83 ns** |   **378,427.1** | **0.2785** |    **4680 B** |
| **PipelineCompletion** | **8**      | **3,795.2 ns** | **189.82 ns** | **99.28 ns** |   **263,491.9** | **0.4272** |    **7208 B** |
