```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V74 2.86GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-VEBMXQ : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

InvocationCount=1  IterationCount=8  LaunchCount=1  
UnrollFactor=1  WarmupCount=3  

```
| Method     | Mean     | Error    | StdDev   | Op/s        |
|----------- |---------:|---------:|---------:|------------:|
| SubmitOnly | 854.4 ns | 682.4 ns | 303.0 ns | 1,170,354.5 |
