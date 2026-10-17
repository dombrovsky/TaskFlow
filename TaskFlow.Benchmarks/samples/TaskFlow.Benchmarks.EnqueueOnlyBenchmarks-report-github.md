```

BenchmarkDotNet v0.15.8, Linux Debian GNU/Linux 13 (trixie)
INTEL XEON PLATINUM 8573C 2.30GHz, 1 CPU, 3 logical and 3 physical cores
.NET SDK 10.0.402
  [Host]     : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4
  Job-VEBMXQ : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v4

InvocationCount=1  IterationCount=8  LaunchCount=1
UnrollFactor=1  WarmupCount=3

```
| Method     | Mean     | Error    | StdDev   | Op/s        |
|----------- |---------:|---------:|---------:|------------:|
| SubmitOnly | 522.7 ns | 80.52 ns | 35.75 ns | 1,913,208.1 |