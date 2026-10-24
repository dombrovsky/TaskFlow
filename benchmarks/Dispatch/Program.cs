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
        var scenarioFilter = args.Length > 2 ? args[2] : "all";
        var keyedFilter = args.Length > 3 ? args[3] : "all";
        var producerFilter = args.Length > 4 ? args[4] : "all";
        var warmupCount = args.Length > 5 ? int.Parse(args[5], CultureInfo.InvariantCulture) : 1000;
        var warmupRounds = args.Length > 6 ? int.Parse(args[6], CultureInfo.InvariantCulture) : 1;
        var minimumWorkers = args.Length > 7 ? int.Parse(args[7], CultureInfo.InvariantCulture) : 32;
        if (!ThreadPool.SetMinThreads(minimumWorkers, 32))
            throw new InvalidOperationException("Cannot configure the requested worker minimum");
        await Console.Error.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
            $"processors={Environment.ProcessorCount},minimum_workers={minimumWorkers},server_gc={System.Runtime.GCSettings.IsServerGC}")).ConfigureAwait(false);
        Console.WriteLine("scenario,keyed,producers,round,operations,ops_per_second,bytes_per_operation,scheduled_drains,cpu_ms,lock_contentions,completed_work_items,threads,gc0,gc1,gc2");
        foreach (var scenario in new[] { "burst-inline", "short-inline", "short-default" })
            foreach (var keyed in new[] { false, true })
                foreach (var producers in new[] { 1, 8 })
                {
                    if ((scenarioFilter != "all" && scenarioFilter != scenario) ||
                        (keyedFilter != "all" && !string.Equals(keyedFilter, keyed.ToString(), StringComparison.OrdinalIgnoreCase)) ||
                        (producerFilter != "all" && producerFilter != producers.ToString(CultureInfo.InvariantCulture)))
                        continue;
                    for (var warmup = 0; warmup < warmupRounds; warmup++)
                        await Measure(scenario, keyed, producers, warmupCount).ConfigureAwait(false);
                    for (var round = 0; round < rounds; round++)
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                        var result = await Measure(scenario, keyed, producers, count).ConfigureAwait(false);
                        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{scenario},{keyed},{producers},{round},{count},{result.Throughput:F0},{result.Bytes:F1},{result.Drains},{result.CpuMs:F1},{result.Contentions},{result.WorkItems},{result.Threads},{result.Gc0},{result.Gc1},{result.Gc2}"));
                    }
                }
    }

    private sealed record Measurement(double Throughput, double Bytes, int Drains, double CpuMs, long Contentions, long WorkItems, int Threads, int Gc0, int Gc1, int Gc2);

    private static async Task<Measurement> Measure(string scenario, bool keyed, int producers, int count)
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
                ? budget.TrySubmit(scheduler, index, static (int _, object? _, CancellationToken _) => new ValueTask<int>(1), null, default)
                : budget.TrySubmit(scheduler, static (object? _, CancellationToken _) => new ValueTask<int>(1), null, default);
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
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        var contentions = Monitor.LockContentionCount;
        var workItems = ThreadPool.CompletedWorkItemCount;
        var gc0 = GC.CollectionCount(0);
        var gc1 = GC.CollectionCount(1);
        var gc2 = GC.CollectionCount(2);
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
        var measurement = new Measurement(count / timer.Elapsed.TotalSeconds, (double)bytes / count, drains,
            (process.TotalProcessorTime - cpu).TotalMilliseconds, Monitor.LockContentionCount - contentions,
            ThreadPool.CompletedWorkItemCount - workItems, ThreadPool.ThreadCount,
            GC.CollectionCount(0) - gc0, GC.CollectionCount(1) - gc1, GC.CollectionCount(2) - gc2);
        var lockSummary = scheduler.GetType().GetProperty("ReviewLockSummary", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(scheduler);
        if (lockSummary != null)
            await Console.Error.WriteLineAsync(lockSummary.ToString()).ConfigureAwait(false);
        return measurement;
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