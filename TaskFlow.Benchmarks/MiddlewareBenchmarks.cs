namespace TaskFlow.Benchmarks;

using BenchmarkDotNet.Attributes;
using System.Threading.Tasks.Flow;

[MemoryDiagnoser]
public class MiddlewareBenchmarks : FlowBenchmark
{
    private ITaskScheduler _scheduler = null!;

    // Zero is the bare terminal baseline; each registration forwards all three phases.
    [Params(0, 1, 2, 4, 8)]
    public int Layers { get; set; }

    public override void Setup()
    {
        base.Setup();
        _scheduler = Owner;
        for (var i = 0; i < Layers; i++)
            _scheduler = _scheduler.UseMiddleware(new ForwardingMiddleware());
    }

    [Benchmark]
    public Task<int> PipelineCompletion()
    {
        return _scheduler.Enqueue(Workload.Sync, null, CancellationToken.None);
    }

    private sealed class ForwardingMiddleware : ITaskSchedulerEnqueueMiddleware,
        ITaskSchedulerExecutionMiddleware, ITaskSchedulerCompletionMiddleware
    {
        public Task<T> InvokeAsync<T>(TaskSchedulerEnqueueContext<T> context, TaskSchedulerEnqueueDelegate<T> next)
        {
            return next(context);
        }

        public ValueTask<T> InvokeAsync<T>(TaskSchedulerOperationContext context, TaskSchedulerExecutionDelegate<T> next)
        {
            return next(context);
        }

        public ValueTask<TaskSchedulerOperationOutcome<T>> InvokeAsync<T>(TaskSchedulerOperationContext context,
            TaskSchedulerOperationOutcome<T> outcome, TaskSchedulerCompletionDelegate<T> next)
        {
            return next(context, outcome);
        }
    }
}