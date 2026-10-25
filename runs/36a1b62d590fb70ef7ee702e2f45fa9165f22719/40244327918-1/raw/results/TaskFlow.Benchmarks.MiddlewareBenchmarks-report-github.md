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
| **PipelineCompletion** | **0**      |   **790.7 ns** | **37.79 ns** | **19.77 ns** | **1,264,628.7** | **0.0305** |     **512 B** |
| **PipelineCompletion** | **1**      | **1,754.7 ns** | **39.58 ns** | **17.57 ns** |   **569,908.6** | **0.0992** |    **1726 B** |
| **PipelineCompletion** | **2**      | **2,149.3 ns** | **23.67 ns** | **12.38 ns** |   **465,258.9** | **0.1373** |    **2352 B** |
| **PipelineCompletion** | **4**      | **2,923.1 ns** | **18.27 ns** |  **9.56 ns** |   **342,103.8** | **0.2136** |    **3600 B** |
| **PipelineCompletion** | **8**      | **4,563.2 ns** | **13.81 ns** |  **7.22 ns** |   **219,145.1** | **0.3586** |    **6096 B** |
