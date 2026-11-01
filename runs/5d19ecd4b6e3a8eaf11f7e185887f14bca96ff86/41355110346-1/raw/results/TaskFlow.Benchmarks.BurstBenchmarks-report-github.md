```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 3.05GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v3

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method         | Mean     | Error    | StdDev   | Op/s        | Gen0   | Gen1   | Allocated |
|--------------- |---------:|---------:|---------:|------------:|-------:|-------:|----------:|
| SubmitAndDrain | 809.3 ns | 25.17 ns | 13.17 ns | 1,235,683.7 | 0.0352 | 0.0195 |     643 B |
