namespace TaskFlow.Benchmarks;

using System.Threading.Tasks.Flow;
using Flow = System.Threading.Tasks.Flow.TaskFlow;

internal static class Workload
{
    public const int BatchSize = 1000;
    public const int Result = 42;

    public static ValueTask<int> Sync(object? state, CancellationToken token)
    {
        // Canceled tokens are deliberately tolerated: TaskFlow invokes accepted work cooperatively.
        return new ValueTask<int>(Result);
    }

    public static async ValueTask<int> Yielding(object? state, CancellationToken token)
    {
        await Task.Yield();
        return Result;
    }

    public static void Check(IEnumerable<int> results)
    {
        var values = results.ToArray();
        if (values.Length != BatchSize || values.Any(value => value != Result))
            throw new InvalidOperationException("Workload lost operations or returned an unexpected result.");
    }
}

// Owners live across invocations; returned tasks are awaited before the next invocation.
public abstract class FlowBenchmark
{
    protected Flow Owner { get; private set; } = null!;

    [BenchmarkDotNet.Attributes.GlobalSetup]
    public virtual void Setup()
    {
        Owner = new Flow();
    }

    [BenchmarkDotNet.Attributes.GlobalCleanup]
    public virtual async Task Cleanup()
    {
        await Owner.DisposeAsync().ConfigureAwait(false);
    }
}

// Keeps a queue turn active without timing assumptions or sleeping worker threads.
internal sealed class QueueGate
{
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Task<int> _blocker = null!;

    public async Task HoldAsync(ITaskScheduler scheduler)
    {
        _blocker = scheduler.Enqueue(async (_, _) =>
        {
            _entered.SetResult();
            await _release.Task.ConfigureAwait(false);
            return Workload.Result;
        }, null, CancellationToken.None);
        await _entered.Task.ConfigureAwait(false);
    }

    public void Release()
    {
        _release.TrySetResult();
    }

    public async Task DrainAsync()
    {
        Release();
        if (await _blocker.ConfigureAwait(false) != Workload.Result)
            throw new InvalidOperationException("Unexpected blocker result.");
    }
}