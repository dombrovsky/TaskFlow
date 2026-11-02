```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 3.24GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method             | Layers | Mean       | Error    | StdDev   | Op/s        | Gen0   | Allocated |
|------------------- |------- |-----------:|---------:|---------:|------------:|-------:|----------:|
| **PipelineCompletion** | **0**      |   **886.7 ns** | **12.37 ns** |  **6.47 ns** | **1,127,821.6** | **0.0305** |     **512 B** |
| **PipelineCompletion** | **1**      | **2,115.6 ns** | **25.20 ns** | **11.19 ns** |   **472,668.5** | **0.0954** |    **1605 B** |
| **PipelineCompletion** | **2**      | **2,410.3 ns** | **16.24 ns** |  **7.21 ns** |   **414,894.3** | **0.1259** |    **2111 B** |
| **PipelineCompletion** | **4**      | **2,895.3 ns** | **56.38 ns** | **29.49 ns** |   **345,392.8** | **0.1831** |    **3120 B** |
| **PipelineCompletion** | **8**      | **4,160.5 ns** | **49.26 ns** | **25.76 ns** |   **240,357.5** | **0.3052** |    **5136 B** |
