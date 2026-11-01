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
| **PipelineCompletion** | **0**      |   **892.9 ns** | **15.82 ns** |  **8.27 ns** | **1,119,953.2** | **0.0315** |     **535 B** |
| **PipelineCompletion** | **1**      | **1,576.3 ns** | **39.93 ns** | **20.88 ns** |   **634,379.9** | **0.0916** |    **1606 B** |
| **PipelineCompletion** | **2**      | **1,766.8 ns** | **16.15 ns** |  **8.45 ns** |   **565,985.4** | **0.1259** |    **2111 B** |
| **PipelineCompletion** | **4**      | **2,207.4 ns** | **26.47 ns** | **11.75 ns** |   **453,012.0** | **0.1831** |    **3120 B** |
| **PipelineCompletion** | **8**      | **3,052.7 ns** | **69.00 ns** | **36.09 ns** |   **327,582.3** | **0.3052** |    **5136 B** |
