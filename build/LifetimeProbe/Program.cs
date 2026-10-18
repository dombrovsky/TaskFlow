namespace TaskFlow.LifetimeProbe;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks.Flow;
using System.Threading.Tasks.Flow.Internal;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly Func<object?, CancellationToken, ValueTask<int>> Sync = (_, _) => new ValueTask<int>(42);

    private static async Task Main(string[] args)
    {
        if (args.Contains("--scope"))
        {
            var lifetime = new TaskSchedulerMiddlewareLifetime();
            await using var lifetimeCleanup = lifetime.ConfigureAwait(false);
            Measure("scope/root", () => { using var scope = lifetime.EnterContext(); });
            using (lifetime.EnterContext())
                Measure("scope/nested", () => { using var scope = lifetime.EnterContext(); });
            Measure("Task/default-context-construction", ConstructTask);
            var local = new AsyncLocal<object?>();
            var first = new object();
            var second = new object();
            local.Value = first;
            Measure("Task/ambient-context-construction", ConstructTask);
            Measure("AsyncLocal/change-and-restore", () => { local.Value = second; local.Value = first; });
            Measure("AsyncLocal/same-value", () => local.Value = first);
            var operation = new PipelineOperation<int>(Sync, null, 0, null, default);
            Measure("activity/add-release", () => { operation.AddActivity(); operation.ReleaseActivity(); });
            return;
        }
        var samples = new List<object>();
        foreach (var scenario in new[] { "layers/0", "layers/1", "layers/2", "layers/4", "layers/8", "yield", "burst", "producers/4", "producers/16", "annotated", "bounded/outstanding", "bounded/pending", "bounded/producers", "keyed", "keyed/producers" })
        {
            var flow = new System.Threading.Tasks.Flow.TaskFlow();
            await using var flowCleanup = flow.ConfigureAwait(false);
            ITaskScheduler scheduler = flow;
            if (scenario.StartsWith("layers/", StringComparison.Ordinal))
                for (var i = 0; i < int.Parse(scenario.AsSpan(7), CultureInfo.InvariantCulture); i++)
                    scheduler = scheduler.UseMiddleware(new Forwarding());
            if (scenario == "annotated")
                scheduler = scheduler.WithOperationName("probe");
            if (scenario.StartsWith("bounded/", StringComparison.Ordinal))
                scheduler = scheduler.WithBoundedAdmission(new BoundedAdmissionOptions
                {
                    Capacity = 32,
                    CapacityMode = scenario == "bounded/pending" ? TaskSchedulerCapacityMode.Pending : TaskSchedulerCapacityMode.Outstanding
                });
            var keyed = scenario.StartsWith("keyed", StringComparison.Ordinal);
            if (keyed)
                scheduler = scheduler.WithKeyedScheduling<int>().WithConcurrencyLimit(4);
            var producers = scenario == "producers/16" ? 16 : scenario.Contains("producers", StringComparison.Ordinal) ? 4 : 1;
            var batched = scenario == "burst" || producers > 1;
            Func<Task> run;
            const int batchSize = 1024;
            if (batched)
                run = async () =>
                {
                    var tasks = new Task<int>[batchSize];
                    var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var submitters = Enumerable.Range(0, producers).Select(producer => Task.Run(async () =>
                    {
                        await start.Task.ConfigureAwait(false);
                        for (var i = producer; i < tasks.Length; i += producers)
                            tasks[i] = keyed ? scheduler.Enqueue(i % 8, () => 42) : scheduler.Enqueue(Sync, null, default);
                    })).ToArray();
                    start.SetResult(true);
                    await Task.WhenAll(submitters).ConfigureAwait(false);
                    var results = await Task.WhenAll(tasks).ConfigureAwait(false);
                    if (results.Any(value => value != 42))
                        throw new InvalidOperationException("Invalid result");
                };
            else
                run = async () =>
                {
                    for (var i = 0; i < batchSize; i++)
                    {
                        var result = keyed ? await scheduler.Enqueue(i % 8, () => 42).ConfigureAwait(false)
                            : await scheduler.Enqueue(scenario == "yield" ? Yield : Sync, null, default).ConfigureAwait(false);
                        if (result != 42)
                            throw new InvalidOperationException("Invalid result");
                    }
                };
            for (var warmup = 0; warmup < 12; warmup++)
                await run().ConfigureAwait(false);
            for (var iteration = 0; iteration < 8; iteration++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var allocated = GC.GetTotalAllocatedBytes(true);
                var start = Stopwatch.GetTimestamp();
                const int batches = 8;
                for (var batch = 0; batch < batches; batch++)
                    await run().ConfigureAwait(false);
                var elapsed = Stopwatch.GetElapsedTime(start).TotalNanoseconds;
                allocated = GC.GetTotalAllocatedBytes(true) - allocated;
                samples.Add(new { scenario, iteration, operations = batches * batchSize, nsPerOperation = elapsed / (batches * batchSize), bytesPerOperation = (double)allocated / (batches * batchSize) });
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(new { runtime = Environment.Version.ToString(), processors = Environment.ProcessorCount, samples }, JsonOptions));
    }

    private static async ValueTask<int> Yield(object? state, CancellationToken token) { await Task.Yield(); return 42; }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "This construction-only probe never starts tasks or creates wait handles; unstarted tasks cannot be disposed.")]
    private static void ConstructTask() => GC.KeepAlive(new Task<int>(static () => 42));

    private static void Measure(string name, Action action)
    {
        for (var i = 0; i < 10000; i++)
            action();
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        const int count = 1000000;
        for (var i = 0; i < count; i++)
            action();
        Console.WriteLine(JsonSerializer.Serialize(new { name, iterations = count, nsPerOperation = Stopwatch.GetElapsedTime(started).TotalNanoseconds / count, bytesPerOperation = (GC.GetAllocatedBytesForCurrentThread() - bytes) / (double)count }));
    }

    private sealed class Forwarding : ITaskSchedulerEnqueueMiddleware, ITaskSchedulerExecutionMiddleware, ITaskSchedulerCompletionMiddleware
    {
        public Task<T> InvokeAsync<T>(TaskSchedulerEnqueueContext<T> context, TaskSchedulerEnqueueDelegate<T> next) => next(context);
        public ValueTask<T> InvokeAsync<T>(TaskSchedulerOperationContext context, TaskSchedulerExecutionDelegate<T> next) => next(context);
        public ValueTask<TaskSchedulerOperationOutcome<T>> InvokeAsync<T>(TaskSchedulerOperationContext context, TaskSchedulerOperationOutcome<T> outcome, TaskSchedulerCompletionDelegate<T> next) => next(context, outcome);
    }
}