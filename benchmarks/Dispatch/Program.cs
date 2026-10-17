namespace TaskFlow.DispatchBenchmarks;

using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks.Flow;

internal static class Program
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters", Justification = "Machine-readable CSV column names.")]
    private static async Task Main(string[] args)
    {
        var count = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 20000;
        var rounds = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 5;
        ThreadPool.SetMinThreads(32, 32);
        Console.WriteLine("scenario,keyed,producers,round,operations,ops_per_second,bytes_per_operation,scheduled_drains");
        foreach (var scenario in new[] { "burst-inline", "short-inline", "short-default" })
            foreach (var keyed in new[] { false, true })
                foreach (var producers in new[] { 1, 8 })
                {
                    await Measure(scenario, keyed, producers, 1000).ConfigureAwait(false);
                    for (var round = 0; round < rounds; round++)
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                        var result = await Measure(scenario, keyed, producers, count).ConfigureAwait(false);
                        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{scenario},{keyed},{producers},{round},{count},{result.Throughput:F0},{result.Bytes:F1},{result.Drains}"));
                    }
                }
    }

    private static async Task<(double Throughput, double Bytes, int Drains)> Measure(string scenario, bool keyed, int producers, int count)
    {
        using var inline = new InlineScheduler(scenario == "burst-inline");
        TaskScheduler backend = scenario == "short-default" ? TaskScheduler.Default : inline;
        var flow = new TaskFlow(new TaskFlowOptions { TaskScheduler = backend });
        await using var flowOwner = flow.ConfigureAwait(false);
        var lifetime = new TaskSchedulerMiddlewareLifetime();
        await using var lifetimeOwner = lifetime.ConfigureAwait(false);
        var budget = new BoundedAdmissionController(new BoundedAdmissionOptions { Capacity = count });
        ITaskScheduler scheduler = keyed
            ? flow.WithKeyedScheduling<int>().WithConcurrencyLimit(count).WithBoundedAdmission(budget)
            : new TaskFlowSchedulerAdapter(backend).WithBoundedAdmission(budget, lifetime);
        var completions = new Task<int>[count];
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var start = scenario == "burst-inline" ? 1 : 0;
        void Submit(int index)
        {
            var attempt = keyed
                ? budget.TrySubmit(scheduler, index, static _ => new ValueTask<int>(1))
                : budget.TrySubmit(scheduler, static () => new ValueTask<int>(1));
            if (attempt.Admission != TaskSchedulerAdmissionResult.Accepted)
                throw new InvalidOperationException("Unexpected rejection");
            completions[index] = attempt.Completion!;
        }
        var workers = Enumerable.Range(0, producers).Select(producer => Task.Run(async () =>
        {
            await go.Task.ConfigureAwait(false);
            for (var index = start + producer; index < count; index += producers)
                Submit(index);
        })).ToArray();
        var allocated = GC.GetTotalAllocatedBytes(precise: true);
        var timer = Stopwatch.StartNew();
        if (start == 1)
        {
            Submit(0);
            await inline.Entered.Task.ConfigureAwait(false);
        }
        go.SetResult();
        await Task.WhenAll(workers).ConfigureAwait(false);
        inline.Release.Set();
        var results = await Task.WhenAll(completions).ConfigureAwait(false);
        await lifetime.DisposeAsync().ConfigureAwait(false);
        await flow.DisposeAsync().ConfigureAwait(false);
        timer.Stop();
        var bytes = GC.GetTotalAllocatedBytes(precise: true) - allocated;
        if (results.Any(value => value != 1))
            throw new InvalidOperationException("Unexpected operation result");
        var drains = (int)scheduler.GetType().GetProperty("ScheduledDrainCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scheduler)!;
        return (count / timer.Elapsed.TotalSeconds, (double)bytes / count, drains);
    }

    private sealed class InlineScheduler(bool blockFirst) : TaskScheduler, IDisposable
    {
        private int _queued;
        public ManualResetEventSlim Release { get; } = new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override IEnumerable<Task> GetScheduledTasks() => Array.Empty<Task>();
        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => TryExecuteTask(task);
        protected override void QueueTask(Task task)
        {
            if (Interlocked.Increment(ref _queued) == 1 && blockFirst)
            {
                Entered.SetResult();
                Release.Wait();
            }
            TryExecuteTask(task);
        }
        public void Dispose() => Release.Dispose();
    }
}