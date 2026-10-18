namespace TaskFlow.Benchmarks;

using BenchmarkDotNet.Attributes;
using System.Threading.Tasks.Flow;

[MemoryDiagnoser]
public class KeyedBenchmarks : FlowBenchmark
{
    private ITaskScheduler _scheduler = null!;

    [Params(1, Workload.BatchSize)]
    public int Keys { get; set; }

    [Params(1, 4)]
    public int Concurrency { get; set; }

    public override void Setup()
    {
        base.Setup();
        _scheduler = Owner.WithKeyedScheduling<int>().WithConcurrencyLimit(Concurrency);
    }

    [Benchmark(OperationsPerInvoke = Workload.BatchSize)]
    public async Task<int[]> DispatchAndDrain()
    {
        var tasks = new Task<int>[Workload.BatchSize];
        for (var i = 0; i < tasks.Length; i++)
            tasks[i] = _scheduler.Enqueue<int, object?, int>(i % Keys,
                static (_, state, token) => Workload.Yielding(state, token), null, CancellationToken.None);
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}