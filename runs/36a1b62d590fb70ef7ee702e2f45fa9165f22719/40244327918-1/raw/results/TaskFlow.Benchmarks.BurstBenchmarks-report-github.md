```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V74 3.69GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method         | Mean     | Error    | StdDev   | Op/s        | Gen0   | Gen1   | Allocated |
|--------------- |---------:|---------:|---------:|------------:|-------:|-------:|----------:|
| SubmitAndDrain | 315.2 ns | 36.01 ns | 15.99 ns | 3,172,665.1 | 0.0391 | 0.0137 |     667 B |
