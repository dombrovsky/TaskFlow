```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method                   | Producers | Mean     | Error    | StdDev   | Op/s        | Gen0   | Gen1   | Allocated |
|------------------------- |---------- |---------:|---------:|---------:|------------:|-------:|-------:|----------:|
| **ConcurrentSubmitAndDrain** | **1**         | **542.6 ns** | **29.16 ns** | **15.25 ns** | **1,843,082.7** | **0.0537** | **0.0293** |     **890 B** |
| **ConcurrentSubmitAndDrain** | **4**         | **719.2 ns** | **63.91 ns** | **33.42 ns** | **1,390,382.4** | **0.0547** | **0.0352** |     **934 B** |
| **ConcurrentSubmitAndDrain** | **16**        | **707.2 ns** | **51.97 ns** | **27.18 ns** | **1,414,013.1** | **0.0547** | **0.0352** |     **937 B** |
