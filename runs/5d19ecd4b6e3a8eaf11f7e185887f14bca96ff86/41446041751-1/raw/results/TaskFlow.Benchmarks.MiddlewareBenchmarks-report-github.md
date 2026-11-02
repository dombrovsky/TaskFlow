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
| **PipelineCompletion** | **0**      |   **860.8 ns** | **29.46 ns** | **15.41 ns** | **1,161,749.5** | **0.0315** |     **536 B** |
| **PipelineCompletion** | **1**      | **1,636.2 ns** | **42.30 ns** | **18.78 ns** |   **611,189.8** | **0.0916** |    **1603 B** |
| **PipelineCompletion** | **2**      | **1,850.1 ns** | **54.79 ns** | **28.65 ns** |   **540,519.3** | **0.1221** |    **2109 B** |
| **PipelineCompletion** | **4**      | **2,269.6 ns** | **19.90 ns** | **10.41 ns** |   **440,602.5** | **0.1869** |    **3119 B** |
| **PipelineCompletion** | **8**      | **3,160.3 ns** | **71.06 ns** | **31.55 ns** |   **316,425.3** | **0.3052** |    **5136 B** |
