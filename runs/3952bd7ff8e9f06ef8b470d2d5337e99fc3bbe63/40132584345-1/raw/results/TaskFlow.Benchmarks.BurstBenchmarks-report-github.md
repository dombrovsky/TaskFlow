```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
INTEL XEON PLATINUM 8573C 3.24GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4
  Job-YZJABG : .NET 10.0.0 (10.0.0, 10.0.25.52411), X64 RyuJIT x86-64-v4

IterationCount=8  LaunchCount=1  WarmupCount=3  

```
| Method         | Mean     | Error   | StdDev  | Op/s        | Gen0   | Gen1   | Allocated |
|--------------- |---------:|--------:|--------:|------------:|-------:|-------:|----------:|
| SubmitAndDrain | 480.9 ns | 6.18 ns | 2.20 ns | 2,079,571.4 | 0.0078 | 0.0020 |     655 B |
