namespace TaskFlow.Benchmarks;

using BenchmarkDotNet.Attributes;
using System.Threading.Tasks.Flow;

public class EnqueueOnlyBenchmarks : FlowBenchmark
{
    private readonly Task<int>[] _tasks = new Task<int>[Workload.BatchSize];

    private QueueGate _gate = null!;

    // Iteration setup/cleanup forces one invocation and unroll factor one.
    [IterationSetup]
    public void HoldQueue()
    {
        _gate = new QueueGate();
        _gate.HoldAsync(Owner).GetAwaiter().GetResult();
    }

    [Benchmark(OperationsPerInvoke = Workload.BatchSize)]
    public void SubmitOnly()
    {
        for (var i = 0; i < _tasks.Length; i++)
            _tasks[i] = Owner.Enqueue(Workload.Sync, null, CancellationToken.None);
    }

    [IterationCleanup]
    public void DrainAndCheck()
    {
        // Release even if the workload failed. Never dispose a flow with a closed gate.
        _gate.Release();
        try
        {
            Workload.Check(Task.WhenAll(_tasks).GetAwaiter().GetResult());
        }
        finally
        {
            _gate.DrainAsync().GetAwaiter().GetResult();
        }
    }
}