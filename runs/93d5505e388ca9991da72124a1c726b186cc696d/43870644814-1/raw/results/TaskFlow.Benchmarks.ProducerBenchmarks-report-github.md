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
| **ConcurrentSubmitAndDrain** | **1**         | **294.3 ns** | **26.31 ns** | **13.76 ns** | **3,398,018.1** | **0.0371** | **0.0156** |     **624 B** |
| **ConcurrentSubmitAndDrain** | **4**         | **456.5 ns** | **27.20 ns** | **12.08 ns** | **2,190,360.1** | **0.0410** | **0.0254** |     **699 B** |
| **ConcurrentSubmitAndDrain** | **16**        | **447.8 ns** | **35.28 ns** | **18.45 ns** | **2,233,076.7** | **0.0420** | **0.0293** |     **697 B** |
