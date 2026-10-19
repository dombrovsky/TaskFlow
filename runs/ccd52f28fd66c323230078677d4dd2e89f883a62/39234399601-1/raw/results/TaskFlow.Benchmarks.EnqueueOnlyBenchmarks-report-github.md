```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V45 4.17GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-VEBMXQ : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

InvocationCount=1  IterationCount=8  LaunchCount=1  
UnrollFactor=1  WarmupCount=3  

```
| Method     | Mean     | Error    | StdDev   | Op/s        |
|----------- |---------:|---------:|---------:|------------:|
| SubmitOnly | 404.4 ns | 182.3 ns | 80.96 ns | 2,472,508.4 |
