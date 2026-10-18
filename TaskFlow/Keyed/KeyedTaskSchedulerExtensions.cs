namespace System.Threading.Tasks.Flow;

using System.Collections.Generic;
using System.Threading.Tasks.Flow.Annotations;
using System.Threading.Tasks.Flow.Internal;

/// <summary>Composes flow-owned keyed execution, execution concurrency, and per-key admission.</summary>
public static class KeyedTaskSchedulerExtensions
{
    /// <summary>Creates a keyed execution branch owned by the original TaskFlow.</summary>
    /// <remarks>Same-key work is FIFO; ready keys are selected round robin. Configure admission after this call.
    /// Thread flows and opaque/custom schedulers are rejected because they cannot provide this execution branch.</remarks>
    public static ITaskScheduler WithKeyedScheduling<TKey>(this ITaskScheduler scheduler, IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        Argument.NotNull(scheduler);
        var pipeline = scheduler as PipelineTaskScheduler ?? new PipelineTaskScheduler(scheduler);
        if (pipeline.Coordinator != null)
            throw new InvalidOperationException("A scheduling coordinator is already configured.");
        var flow = pipeline.TerminalFlow ?? throw new NotSupportedException("Keyed execution branches require a TaskFlow owner.");
        var coordinator = (KeyedCoordinator<TKey>)pipeline.Lifetime!.Register(new KeyedCoordinator<TKey>.Factory(comparer ?? EqualityComparer<TKey>.Default));
        return pipeline.WithKeyedScheduling(coordinator, new TaskFlowSchedulerAdapter(flow.Options.TaskScheduler));
    }

    /// <summary>Limits dispatched operations until their underlying pipeline work settles.</summary>
    /// <remarks>On a TaskFlow this creates an independent, flow-owned concurrent execution branch. On a keyed branch,
    /// busy-key waiters never reserve execution slots. Descendants share the limit; sibling branches may configure independent limits.</remarks>
    public static ITaskScheduler WithConcurrencyLimit(this ITaskScheduler scheduler, int maxConcurrency)
    {
        Argument.NotNull(scheduler);
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrency);
#else
        if (maxConcurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
#endif
        var pipeline = scheduler as PipelineTaskScheduler ?? new PipelineTaskScheduler(scheduler);
        if (pipeline.Coordinator == null)
        {
            var flow = pipeline.TerminalFlow ?? throw new NotSupportedException("Concurrent execution branches require a TaskFlow owner.");
            var coordinator = (ConcurrentCoordinator)pipeline.Lifetime!.Register(new ConcurrentCoordinator.Factory());
            pipeline = pipeline.WithKeyedScheduling(coordinator, new TaskFlowSchedulerAdapter(flow.Options.TaskScheduler));
        }
        return pipeline.Append(new ExecutionPolicy(maxConcurrency));
    }

    /// <summary>Adds an independent capacity budget for each key, checked atomically with other coordinated budgets.</summary>
    public static ITaskScheduler WithPerKeyBoundedAdmission(this ITaskScheduler scheduler, BoundedAdmissionOptions options)
        => scheduler.WithPerKeyBoundedAdmission(new BoundedAdmissionController(options));

    /// <summary>Adds controller-identified capacity for each key. Descendant snapshots share each key's budget.</summary>
    public static ITaskScheduler WithPerKeyBoundedAdmission(this ITaskScheduler scheduler, BoundedAdmissionController admission)
    {
        Argument.NotNull(scheduler);
        Argument.NotNull(admission);
        if (!(scheduler is PipelineTaskScheduler pipeline) || pipeline.Coordinator?.RequiresKey != true)
            throw new InvalidOperationException("Per-key admission requires keyed scheduling.");
        return pipeline.Append(new AdmissionPolicy(admission, perKey: true));
    }

    internal static PipelineTaskScheduler Validate<TKey>(ITaskScheduler scheduler, TKey key) where TKey : notnull
    {
        Argument.NotNull(scheduler);
        if (!(scheduler is PipelineTaskScheduler pipeline))
            throw new InvalidOperationException("A matching native keyed pipeline is required.");
        pipeline.ValidateKey(key);
        return pipeline;
    }

}