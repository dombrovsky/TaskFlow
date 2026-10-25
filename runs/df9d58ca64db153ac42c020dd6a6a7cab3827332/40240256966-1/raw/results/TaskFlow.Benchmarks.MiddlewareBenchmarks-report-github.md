```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V45 2.60GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method             | Layers | Mean       | Error     | StdDev   | Op/s        | Gen0   | Allocated |
|------------------- |------- |-----------:|----------:|---------:|------------:|-------:|----------:|
| **PipelineCompletion** | **0**      |   **654.7 ns** |  **48.18 ns** | **25.20 ns** | **1,527,480.6** | **0.0305** |     **512 B** |
| **PipelineCompletion** | **1**      | **1,399.5 ns** |  **31.87 ns** | **14.15 ns** |   **714,563.4** | **0.1068** |    **1792 B** |
| **PipelineCompletion** | **2**      | **1,638.7 ns** |  **36.32 ns** | **16.13 ns** |   **610,227.0** | **0.1450** |    **2416 B** |
| **PipelineCompletion** | **4**      | **2,267.2 ns** | **110.91 ns** | **58.01 ns** |   **441,065.3** | **0.2174** |    **3664 B** |
| **PipelineCompletion** | **8**      | **3,503.1 ns** | **189.93 ns** | **99.34 ns** |   **285,458.1** | **0.3662** |    **6160 B** |
