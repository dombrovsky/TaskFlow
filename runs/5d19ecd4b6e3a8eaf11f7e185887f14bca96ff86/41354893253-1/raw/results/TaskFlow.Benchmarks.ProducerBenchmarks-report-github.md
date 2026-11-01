```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V74 3.69GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method                   | Producers | Mean     | Error    | StdDev   | Op/s        | Gen0   | Gen1   | Allocated |
|------------------------- |---------- |---------:|---------:|---------:|------------:|-------:|-------:|----------:|
| **ConcurrentSubmitAndDrain** | **1**         | **416.4 ns** |  **8.82 ns** |  **3.92 ns** | **2,401,688.9** | **0.0371** | **0.0156** |     **624 B** |
| **ConcurrentSubmitAndDrain** | **4**         | **668.6 ns** | **32.54 ns** | **14.45 ns** | **1,495,660.3** | **0.0391** | **0.0195** |     **665 B** |
| **ConcurrentSubmitAndDrain** | **16**        | **681.7 ns** |  **5.12 ns** |  **2.68 ns** | **1,466,921.9** | **0.0391** | **0.0273** |     **664 B** |
