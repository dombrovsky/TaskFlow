```

BenchmarkDotNet v0.15.8, Linux Debian GNU/Linux 13 (trixie)
INTEL XEON PLATINUM 8573C 2.30GHz, 1 CPU, 3 logical and 3 physical cores
.NET SDK 10.0.402
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4
  Job-YZJABG : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4

IterationCount=8  LaunchCount=1  WarmupCount=3

```
| Method         | Mean     | Error    | StdDev   | Op/s        | Gen0   | Allocated |
|--------------- |---------:|---------:|---------:|------------:|-------:|----------:|
| SubmitAndDrain | 793.2 ns | 248.4 ns | 129.9 ns | 1,260,739.5 | 0.0039 |     848 B |