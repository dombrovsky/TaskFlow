```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V74 2.60GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method             | Layers | Mean       | Error    | StdDev   | Op/s        | Gen0   | Allocated |
|------------------- |------- |-----------:|---------:|---------:|------------:|-------:|----------:|
| **PipelineCompletion** | **0**      |   **768.3 ns** |  **7.75 ns** |  **4.05 ns** | **1,301,509.6** | **0.0305** |     **512 B** |
| **PipelineCompletion** | **1**      | **1,493.7 ns** | **42.76 ns** | **22.37 ns** |   **669,490.9** | **0.0916** |    **1573 B** |
| **PipelineCompletion** | **2**      | **1,689.5 ns** | **54.55 ns** | **24.22 ns** |   **591,892.2** | **0.1221** |    **2055 B** |
| **PipelineCompletion** | **4**      | **2,041.5 ns** | **20.74 ns** |  **9.21 ns** |   **489,837.8** | **0.1793** |    **3016 B** |
| **PipelineCompletion** | **8**      | **2,927.7 ns** | **10.99 ns** |  **4.88 ns** |   **341,566.2** | **0.2899** |    **4936 B** |
