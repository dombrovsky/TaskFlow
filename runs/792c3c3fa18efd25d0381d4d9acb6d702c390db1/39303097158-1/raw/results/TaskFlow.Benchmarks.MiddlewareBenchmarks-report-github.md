```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 2.60GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method             | Layers | Mean       | Error    | StdDev   | Op/s        | Gen0   | Allocated |
|------------------- |------- |-----------:|---------:|---------:|------------:|-------:|----------:|
| **PipelineCompletion** | **0**      |   **947.5 ns** | **20.76 ns** |  **9.22 ns** | **1,055,463.7** | **0.0305** |     **512 B** |
| **PipelineCompletion** | **1**      | **2,334.9 ns** | **12.44 ns** |  **5.52 ns** |   **428,288.6** | **0.1068** |    **1792 B** |
| **PipelineCompletion** | **2**      | **2,798.9 ns** | **42.99 ns** | **22.48 ns** |   **357,277.5** | **0.1373** |    **2424 B** |
| **PipelineCompletion** | **4**      | **3,799.4 ns** | **74.41 ns** | **38.92 ns** |   **263,198.1** | **0.2213** |    **3688 B** |
| **PipelineCompletion** | **8**      | **5,664.1 ns** | **98.80 ns** | **51.67 ns** |   **176,550.6** | **0.3662** |    **6216 B** |
