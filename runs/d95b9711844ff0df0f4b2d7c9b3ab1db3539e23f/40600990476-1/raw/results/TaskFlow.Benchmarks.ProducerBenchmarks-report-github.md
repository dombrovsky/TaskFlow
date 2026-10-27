```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
INTEL XEON PLATINUM 8573C 3.00GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method                   | Producers | Mean     | Error     | StdDev   | Op/s        | Gen0   | Gen1   | Allocated |
|------------------------- |---------- |---------:|----------:|---------:|------------:|-------:|-------:|----------:|
| **ConcurrentSubmitAndDrain** | **1**         | **429.0 ns** |  **17.02 ns** |  **6.07 ns** | **2,331,122.0** | **0.0059** |      **-** |     **614 B** |
| **ConcurrentSubmitAndDrain** | **4**         | **728.1 ns** | **138.87 ns** | **72.63 ns** | **1,373,355.0** | **0.0078** |      **-** |     **695 B** |
| **ConcurrentSubmitAndDrain** | **16**        | **674.0 ns** |  **46.10 ns** | **20.47 ns** | **1,483,683.1** | **0.0078** | **0.0039** |     **700 B** |
