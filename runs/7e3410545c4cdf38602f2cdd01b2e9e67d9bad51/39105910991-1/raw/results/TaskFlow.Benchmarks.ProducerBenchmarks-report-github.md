```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V74 2.60GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method                   | Producers | Mean     | Error    | StdDev   | Op/s        | Gen0   | Gen1   | Allocated |
|------------------------- |---------- |---------:|---------:|---------:|------------:|-------:|-------:|----------:|
| **ConcurrentSubmitAndDrain** | **1**         | **291.1 ns** | **24.37 ns** | **12.75 ns** | **3,435,457.2** | **0.0391** | **0.0137** |     **659 B** |
| **ConcurrentSubmitAndDrain** | **4**         | **430.5 ns** | **16.10 ns** |  **8.42 ns** | **2,322,741.5** | **0.0420** | **0.0269** |     **697 B** |
| **ConcurrentSubmitAndDrain** | **16**        | **440.6 ns** | **74.90 ns** | **39.18 ns** | **2,269,552.1** | **0.0410** | **0.0293** |     **698 B** |
