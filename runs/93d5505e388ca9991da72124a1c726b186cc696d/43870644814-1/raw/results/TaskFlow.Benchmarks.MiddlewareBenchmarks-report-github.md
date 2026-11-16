```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V74 3.69GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method             | Layers | Mean       | Error    | StdDev   | Op/s        | Gen0   | Allocated |
|------------------- |------- |-----------:|---------:|---------:|------------:|-------:|----------:|
| **PipelineCompletion** | **0**      |   **797.9 ns** | **23.76 ns** | **12.43 ns** | **1,253,278.8** | **0.0305** |     **512 B** |
| **PipelineCompletion** | **1**      | **1,667.6 ns** | **48.96 ns** | **25.60 ns** |   **599,676.0** | **0.0916** |    **1602 B** |
| **PipelineCompletion** | **2**      | **1,885.9 ns** | **64.85 ns** | **28.79 ns** |   **530,244.0** | **0.1221** |    **2107 B** |
| **PipelineCompletion** | **4**      | **2,336.3 ns** | **31.91 ns** | **14.17 ns** |   **428,025.1** | **0.1831** |    **3118 B** |
| **PipelineCompletion** | **8**      | **3,328.1 ns** | **52.94 ns** | **27.69 ns** |   **300,468.4** | **0.3052** |    **5136 B** |
