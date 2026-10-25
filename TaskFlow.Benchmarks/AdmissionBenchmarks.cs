namespace TaskFlow.Benchmarks;

using BenchmarkDotNet.Attributes;
using System.Threading.Tasks.Flow;

[MemoryDiagnoser]
public class AdmissionAcceptedBenchmarks : FlowBenchmark
{
    private ITaskScheduler _scheduler = null!;

    public override void Setup()
    {
        base.Setup();
        _scheduler = Owner.WithBoundedAdmission(new BoundedAdmissionOptions { Capacity = 1 });
    }

    [Benchmark]
    public Task<int> AcceptedCompletion()
    {
        return _scheduler.Enqueue(Workload.Sync, null, CancellationToken.None);
    }
}

[MemoryDiagnoser]
public class AdmissionRejectedBenchmarks : FlowBenchmark
{
    private ITaskScheduler _scheduler = null!;
    private QueueGate _gate = null!;

    public override void Setup()
    {
        base.Setup();
        _scheduler = Owner.WithBoundedAdmission(new BoundedAdmissionOptions
        {
            Capacity = 1,
            AdmissionMode = TaskSchedulerAdmissionMode.Reject
        });
        _gate = new QueueGate();
        _gate.HoldAsync(_scheduler).GetAwaiter().GetResult();
    }

    [Benchmark]
    public async Task<int> RejectedCompletion()
    {
        try
        {
            await _scheduler.Enqueue(Workload.Sync, null, CancellationToken.None).ConfigureAwait(false);
        }
        catch (AdmissionRejectedException)
        {
            return Workload.Result;
        }
        throw new InvalidOperationException("Full admission unexpectedly accepted an operation.");
    }

    public override async Task Cleanup()
    {
        await _gate.DrainAsync().ConfigureAwait(false);
        await base.Cleanup().ConfigureAwait(false);
    }
}

public class AdmissionContendedBenchmarks : FlowBenchmark
{
    private ITaskScheduler _scheduler = null!;
    private QueueGate _gate = null!;

    public override void Setup()
    {
        base.Setup();
        _scheduler = Owner.WithBoundedAdmission(new BoundedAdmissionOptions { Capacity = 1 });
    }

    [IterationSetup]
    public void FillCapacity()
    {
        _gate = new QueueGate();
        _gate.HoldAsync(_scheduler).GetAwaiter().GetResult();
    }

    [Benchmark(OperationsPerInvoke = Workload.BatchSize)]
    public async Task<int[]> WaitAndDrain()
    {
        var tasks = new Task<int>[Workload.BatchSize];
        try
        {
            for (var i = 0; i < tasks.Length; i++)
                tasks[i] = _scheduler.Enqueue(Workload.Sync, null, CancellationToken.None);
        }
        finally
        {
            _gate.Release();
        }
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    [IterationCleanup]
    public void ReleaseQueue()
    {
        _gate.DrainAsync().GetAwaiter().GetResult();
    }
}