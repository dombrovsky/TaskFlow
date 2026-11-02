```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V74 2.84GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method             | Layers | Mean       | Error    | StdDev   | Op/s        | Gen0   | Allocated |
|------------------- |------- |-----------:|---------:|---------:|------------:|-------:|----------:|
| **PipelineCompletion** | **0**      |   **958.4 ns** |  **7.70 ns** |  **3.42 ns** | **1,043,382.9** | **0.0305** |     **512 B** |
| **PipelineCompletion** | **1**      | **2,055.0 ns** | **36.29 ns** | **18.98 ns** |   **486,606.4** | **0.0954** |    **1605 B** |
| **PipelineCompletion** | **2**      | **2,324.8 ns** | **38.18 ns** | **19.97 ns** |   **430,151.0** | **0.1221** |    **2110 B** |
| **PipelineCompletion** | **4**      | **2,866.8 ns** | **99.61 ns** | **52.10 ns** |   **348,824.4** | **0.1831** |    **3120 B** |
| **PipelineCompletion** | **8**      | **4,110.2 ns** | **49.27 ns** | **25.77 ns** |   **243,298.2** | **0.3052** |    **5136 B** |
