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
| **PipelineCompletion** | **0**      |   **793.6 ns** | **21.33 ns** | **11.16 ns** | **1,260,130.3** | **0.0305** |     **512 B** |
| **PipelineCompletion** | **1**      | **1,922.4 ns** | **14.03 ns** |  **7.34 ns** |   **520,191.1** | **0.1106** |    **1850 B** |
| **PipelineCompletion** | **2**      | **2,291.3 ns** | **31.73 ns** | **16.60 ns** |   **436,432.2** | **0.1488** |    **2478 B** |
| **PipelineCompletion** | **4**      | **3,100.4 ns** | **38.73 ns** | **20.25 ns** |   **322,541.2** | **0.2136** |    **3728 B** |
| **PipelineCompletion** | **8**      | **4,768.6 ns** | **81.38 ns** | **42.56 ns** |   **209,703.7** | **0.3662** |    **6224 B** |
