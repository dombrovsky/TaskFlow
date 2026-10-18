namespace TaskFlow.Benchmarks;

using BenchmarkDotNet.Attributes;
using System.Threading.Tasks.Flow;

public enum CallerToken
{
    None,
    Live,
    CanceledCooperative
}

[MemoryDiagnoser]
public class SequentialBenchmarks : FlowBenchmark
{
    private CancellationTokenSource? _source;
    private CancellationToken _token;

    [Params(CallerToken.None, CallerToken.Live, CallerToken.CanceledCooperative)]
    public CallerToken Token { get; set; }

    public override void Setup()
    {
        base.Setup();
        if (Token != CallerToken.None)
        {
            _source = new CancellationTokenSource();
            if (Token == CallerToken.CanceledCooperative)
                _source.Cancel();
            _token = _source.Token;
        }
    }

    [Benchmark(Baseline = true)]
    public Task<int> SyncCompletion()
    {
        return Owner.Enqueue(Workload.Sync, null, _token);
    }

    [Benchmark]
    public Task<int> AsyncCompletion()
    {
        return Owner.Enqueue(Workload.Yielding, null, _token);
    }

    public override async Task Cleanup()
    {
        await base.Cleanup().ConfigureAwait(false);
        _source?.Dispose();
    }
}

[MemoryDiagnoser]
public class BurstBenchmarks : FlowBenchmark
{
    [Benchmark(OperationsPerInvoke = Workload.BatchSize)]
    public async Task<int[]> SubmitAndDrain()
    {
        var tasks = new Task<int>[Workload.BatchSize];
        for (var i = 0; i < tasks.Length; i++)
            tasks[i] = Owner.Enqueue(Workload.Sync, null, CancellationToken.None);
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}

[MemoryDiagnoser]
public class ProducerBenchmarks : FlowBenchmark
{
    [Params(1, 4, 16)]
    public int Producers { get; set; }

    [Benchmark(OperationsPerInvoke = Workload.BatchSize)]
    public async Task<int[]> ConcurrentSubmitAndDrain()
    {
        var operations = new Task<int>[Workload.BatchSize];
        var producers = new Task[Producers];
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        for (var producer = 0; producer < Producers; producer++)
        {
            var index = producer;
            producers[producer] = Task.Run(async () =>
            {
                await start.Task.ConfigureAwait(false);
                for (var i = index; i < operations.Length; i += Producers)
                    operations[i] = Owner.Enqueue(Workload.Sync, null, CancellationToken.None);
            });
        }
        start.SetResult();
        await Task.WhenAll(producers).ConfigureAwait(false);
        return await Task.WhenAll(operations).ConfigureAwait(false);
    }
}