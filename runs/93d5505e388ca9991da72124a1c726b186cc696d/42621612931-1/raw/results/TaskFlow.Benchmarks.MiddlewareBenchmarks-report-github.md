```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 2.60GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method             | Layers | Mean       | Error     | StdDev   | Op/s        | Gen0   | Allocated |
|------------------- |------- |-----------:|----------:|---------:|------------:|-------:|----------:|
| **PipelineCompletion** | **0**      |   **966.1 ns** |  **30.38 ns** | **13.49 ns** | **1,035,110.8** | **0.0305** |     **512 B** |
| **PipelineCompletion** | **1**      | **1,964.6 ns** |  **42.54 ns** | **18.89 ns** |   **509,011.6** | **0.0916** |    **1605 B** |
| **PipelineCompletion** | **2**      | **2,224.9 ns** |  **34.38 ns** | **17.98 ns** |   **449,466.8** | **0.1259** |    **2111 B** |
| **PipelineCompletion** | **4**      | **2,885.5 ns** | **110.03 ns** | **57.55 ns** |   **346,560.1** | **0.1831** |    **3120 B** |
| **PipelineCompletion** | **8**      | **4,034.3 ns** |  **36.83 ns** | **19.26 ns** |   **247,877.3** | **0.3052** |    **5136 B** |
