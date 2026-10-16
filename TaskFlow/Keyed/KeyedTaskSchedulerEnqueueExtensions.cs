namespace System.Threading.Tasks.Flow;

using System.Threading.Tasks.Flow.Annotations;
using System.Threading.Tasks.Flow.Internal;

/// <summary>Supplies immutable scheduling keys independently of operation state.</summary>
public static class KeyedTaskSchedulerEnqueueExtensions
{
    /// <summary>Enqueues a keyed operation with typed state and the original scheduling key.</summary>
    public static Task<TResult> Enqueue<TKey, TState, TResult>(this ITaskScheduler scheduler, TKey key,
        Func<TKey, TState, CancellationToken, ValueTask<TResult>> taskFunc, TState state, CancellationToken cancellationToken)
        where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return KeyedTaskSchedulerExtensions.Validate(scheduler, key).SubmitKeyed(key, (s, token) => taskFunc(key, (TState)s!, token), state, null, cancellationToken);
    }

    /// <summary>Enqueues a keyed operation with typed state and the original scheduling key.</summary>
    public static Task<TResult> Enqueue<TKey, TState, TResult>(this ITaskScheduler scheduler, TKey key,
        Func<TKey, TState, CancellationToken, ValueTask<TResult>> taskFunc, TState state)
        where TKey : notnull
        => scheduler.Enqueue<TKey, TState, TResult>(key, taskFunc, state, CancellationToken.None);

    /// <summary>Enqueues a keyed Task operation with typed state.</summary>
    public static Task<TResult> Enqueue<TKey, TState, TResult>(this ITaskScheduler scheduler, TKey key,
        Func<TKey, TState, CancellationToken, Task<TResult>> taskFunc, TState state, CancellationToken cancellationToken)
        where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return scheduler.Enqueue(key, (TKey k, TState s, CancellationToken token) => new ValueTask<TResult>(taskFunc(k, s, token)), state, cancellationToken);
    }

    /// <summary>Enqueues a keyed Task operation with typed state.</summary>
    public static Task<TResult> Enqueue<TKey, TState, TResult>(this ITaskScheduler scheduler, TKey key,
        Func<TKey, TState, CancellationToken, Task<TResult>> taskFunc, TState state)
        where TKey : notnull
        => scheduler.Enqueue<TKey, TState, TResult>(key, taskFunc, state, CancellationToken.None);

    /// <summary>Enqueues a keyed Action operation.</summary>
    public static Task Enqueue<TKey>(this ITaskScheduler scheduler, TKey key, Action taskFunc, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return scheduler.Enqueue<TKey, object?, Unit>(key, (TKey k, object? _, CancellationToken token) => { taskFunc(); return new ValueTask<Unit>(default(Unit)); }, null, cancellationToken);
    }

    /// <summary>Enqueues a keyed Action operation.</summary>
    public static Task Enqueue<TKey>(this ITaskScheduler scheduler, TKey key, Action taskFunc) where TKey : notnull
        => scheduler.Enqueue<TKey>(key, taskFunc, CancellationToken.None);

    /// <summary>Enqueues a keyed synchronous operation.</summary>
    public static Task<TResult> Enqueue<TKey, TResult>(this ITaskScheduler scheduler, TKey key, Func<TResult> taskFunc, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return scheduler.Enqueue<TKey, object?, TResult>(key, (TKey k, object? _, CancellationToken token) => { return new ValueTask<TResult>(taskFunc()); }, null, cancellationToken);
    }

    /// <summary>Enqueues a keyed synchronous operation.</summary>
    public static Task<TResult> Enqueue<TKey, TResult>(this ITaskScheduler scheduler, TKey key, Func<TResult> taskFunc) where TKey : notnull
        => scheduler.Enqueue<TKey, TResult>(key, taskFunc, CancellationToken.None);

    /// <summary>Enqueues a keyed Task operation.</summary>
    public static Task Enqueue<TKey>(this ITaskScheduler scheduler, TKey key, Func<Task> taskFunc, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return scheduler.Enqueue<TKey, object?, Unit>(key, async ValueTask<Unit> (TKey k, object? _, CancellationToken token) => { await taskFunc().ConfigureAwait(true); return default(Unit); }, null, cancellationToken);
    }

    /// <summary>Enqueues a keyed Task operation.</summary>
    public static Task Enqueue<TKey>(this ITaskScheduler scheduler, TKey key, Func<Task> taskFunc) where TKey : notnull
        => scheduler.Enqueue<TKey>(key, taskFunc, CancellationToken.None);

    /// <summary>Enqueues a keyed Task operation.</summary>
    public static Task Enqueue<TKey>(this ITaskScheduler scheduler, TKey key, Func<CancellationToken, Task> taskFunc, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return scheduler.Enqueue<TKey, object?, Unit>(key, async ValueTask<Unit> (TKey k, object? _, CancellationToken token) => { await taskFunc(token).ConfigureAwait(true); return default(Unit); }, null, cancellationToken);
    }

    /// <summary>Enqueues a keyed Task operation.</summary>
    public static Task Enqueue<TKey>(this ITaskScheduler scheduler, TKey key, Func<CancellationToken, Task> taskFunc) where TKey : notnull
        => scheduler.Enqueue<TKey>(key, taskFunc, CancellationToken.None);

    /// <summary>Enqueues a keyed Task result-producing operation.</summary>
    public static Task<TResult> Enqueue<TKey, TResult>(this ITaskScheduler scheduler, TKey key, Func<Task<TResult>> taskFunc, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return scheduler.Enqueue<TKey, object?, TResult>(key, (TKey k, object? _, CancellationToken token) => { return new ValueTask<TResult>(taskFunc()); }, null, cancellationToken);
    }

    /// <summary>Enqueues a keyed Task result-producing operation.</summary>
    public static Task<TResult> Enqueue<TKey, TResult>(this ITaskScheduler scheduler, TKey key, Func<Task<TResult>> taskFunc) where TKey : notnull
        => scheduler.Enqueue<TKey, TResult>(key, taskFunc, CancellationToken.None);

    /// <summary>Enqueues a keyed Task result-producing operation.</summary>
    public static Task<TResult> Enqueue<TKey, TResult>(this ITaskScheduler scheduler, TKey key, Func<CancellationToken, Task<TResult>> taskFunc, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return scheduler.Enqueue<TKey, object?, TResult>(key, (TKey k, object? _, CancellationToken token) => { return new ValueTask<TResult>(taskFunc(token)); }, null, cancellationToken);
    }

    /// <summary>Enqueues a keyed Task result-producing operation.</summary>
    public static Task<TResult> Enqueue<TKey, TResult>(this ITaskScheduler scheduler, TKey key, Func<CancellationToken, Task<TResult>> taskFunc) where TKey : notnull
        => scheduler.Enqueue<TKey, TResult>(key, taskFunc, CancellationToken.None);

    /// <summary>Enqueues a keyed ValueTask operation.</summary>
    public static Task Enqueue<TKey>(this ITaskScheduler scheduler, TKey key, Func<ValueTask> taskFunc, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return scheduler.Enqueue<TKey, object?, Unit>(key, async ValueTask<Unit> (TKey k, object? _, CancellationToken token) => { await taskFunc().ConfigureAwait(true); return default(Unit); }, null, cancellationToken);
    }

    /// <summary>Enqueues a keyed ValueTask operation.</summary>
    public static Task Enqueue<TKey>(this ITaskScheduler scheduler, TKey key, Func<ValueTask> taskFunc) where TKey : notnull
        => scheduler.Enqueue<TKey>(key, taskFunc, CancellationToken.None);

    /// <summary>Enqueues a keyed ValueTask operation.</summary>
    public static Task Enqueue<TKey>(this ITaskScheduler scheduler, TKey key, Func<CancellationToken, ValueTask> taskFunc, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return scheduler.Enqueue<TKey, object?, Unit>(key, async ValueTask<Unit> (TKey k, object? _, CancellationToken token) => { await taskFunc(token).ConfigureAwait(true); return default(Unit); }, null, cancellationToken);
    }

    /// <summary>Enqueues a keyed ValueTask operation.</summary>
    public static Task Enqueue<TKey>(this ITaskScheduler scheduler, TKey key, Func<CancellationToken, ValueTask> taskFunc) where TKey : notnull
        => scheduler.Enqueue<TKey>(key, taskFunc, CancellationToken.None);

    /// <summary>Enqueues a keyed ValueTask result-producing operation.</summary>
    public static Task<TResult> Enqueue<TKey, TResult>(this ITaskScheduler scheduler, TKey key, Func<ValueTask<TResult>> taskFunc, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return scheduler.Enqueue<TKey, object?, TResult>(key, (TKey k, object? _, CancellationToken token) => { return taskFunc(); }, null, cancellationToken);
    }

    /// <summary>Enqueues a keyed ValueTask result-producing operation.</summary>
    public static Task<TResult> Enqueue<TKey, TResult>(this ITaskScheduler scheduler, TKey key, Func<ValueTask<TResult>> taskFunc) where TKey : notnull
        => scheduler.Enqueue<TKey, TResult>(key, taskFunc, CancellationToken.None);

    /// <summary>Enqueues a keyed ValueTask result-producing operation.</summary>
    public static Task<TResult> Enqueue<TKey, TResult>(this ITaskScheduler scheduler, TKey key, Func<CancellationToken, ValueTask<TResult>> taskFunc, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return scheduler.Enqueue<TKey, object?, TResult>(key, (TKey k, object? _, CancellationToken token) => { return taskFunc(token); }, null, cancellationToken);
    }

    /// <summary>Enqueues a keyed ValueTask result-producing operation.</summary>
    public static Task<TResult> Enqueue<TKey, TResult>(this ITaskScheduler scheduler, TKey key, Func<CancellationToken, ValueTask<TResult>> taskFunc) where TKey : notnull
        => scheduler.Enqueue<TKey, TResult>(key, taskFunc, CancellationToken.None);

    /// <summary>Enqueues a keyed Action operation.</summary>
    public static Task Enqueue<TKey>(this ITaskScheduler scheduler, TKey key, Action<TKey> taskFunc, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return scheduler.Enqueue<TKey, object?, Unit>(key, (TKey k, object? _, CancellationToken token) => { taskFunc(k); return new ValueTask<Unit>(default(Unit)); }, null, cancellationToken);
    }

    /// <summary>Enqueues a keyed Action operation.</summary>
    public static Task Enqueue<TKey>(this ITaskScheduler scheduler, TKey key, Action<TKey> taskFunc) where TKey : notnull
        => scheduler.Enqueue<TKey>(key, taskFunc, CancellationToken.None);

    /// <summary>Enqueues a keyed synchronous operation.</summary>
    public static Task<TResult> Enqueue<TKey, TResult>(this ITaskScheduler scheduler, TKey key, Func<TKey, TResult> taskFunc, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return scheduler.Enqueue<TKey, object?, TResult>(key, (TKey k, object? _, CancellationToken token) => { return new ValueTask<TResult>(taskFunc(k)); }, null, cancellationToken);
    }

    /// <summary>Enqueues a keyed synchronous operation.</summary>
    public static Task<TResult> Enqueue<TKey, TResult>(this ITaskScheduler scheduler, TKey key, Func<TKey, TResult> taskFunc) where TKey : notnull
        => scheduler.Enqueue<TKey, TResult>(key, taskFunc, CancellationToken.None);

    /// <summary>Enqueues a keyed Task operation.</summary>
    public static Task Enqueue<TKey>(this ITaskScheduler scheduler, TKey key, Func<TKey, Task> taskFunc, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return scheduler.Enqueue<TKey, object?, Unit>(key, async ValueTask<Unit> (TKey k, object? _, CancellationToken token) => { await taskFunc(k).ConfigureAwait(true); return default(Unit); }, null, cancellationToken);
    }

    /// <summary>Enqueues a keyed Task operation.</summary>
    public static Task Enqueue<TKey>(this ITaskScheduler scheduler, TKey key, Func<TKey, Task> taskFunc) where TKey : notnull
        => scheduler.Enqueue<TKey>(key, taskFunc, CancellationToken.None);

    /// <summary>Enqueues a keyed Task operation.</summary>
    public static Task Enqueue<TKey>(this ITaskScheduler scheduler, TKey key, Func<TKey, CancellationToken, Task> taskFunc, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return scheduler.Enqueue<TKey, object?, Unit>(key, async ValueTask<Unit> (TKey k, object? _, CancellationToken token) => { await taskFunc(k, token).ConfigureAwait(true); return default(Unit); }, null, cancellationToken);
    }

    /// <summary>Enqueues a keyed Task operation.</summary>
    public static Task Enqueue<TKey>(this ITaskScheduler scheduler, TKey key, Func<TKey, CancellationToken, Task> taskFunc) where TKey : notnull
        => scheduler.Enqueue<TKey>(key, taskFunc, CancellationToken.None);

    /// <summary>Enqueues a keyed Task result-producing operation.</summary>
    public static Task<TResult> Enqueue<TKey, TResult>(this ITaskScheduler scheduler, TKey key, Func<TKey, Task<TResult>> taskFunc, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return scheduler.Enqueue<TKey, object?, TResult>(key, (TKey k, object? _, CancellationToken token) => { return new ValueTask<TResult>(taskFunc(k)); }, null, cancellationToken);
    }

    /// <summary>Enqueues a keyed Task result-producing operation.</summary>
    public static Task<TResult> Enqueue<TKey, TResult>(this ITaskScheduler scheduler, TKey key, Func<TKey, Task<TResult>> taskFunc) where TKey : notnull
        => scheduler.Enqueue<TKey, TResult>(key, taskFunc, CancellationToken.None);

    /// <summary>Enqueues a keyed Task result-producing operation.</summary>
    public static Task<TResult> Enqueue<TKey, TResult>(this ITaskScheduler scheduler, TKey key, Func<TKey, CancellationToken, Task<TResult>> taskFunc, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return scheduler.Enqueue<TKey, object?, TResult>(key, (TKey k, object? _, CancellationToken token) => { return new ValueTask<TResult>(taskFunc(k, token)); }, null, cancellationToken);
    }

    /// <summary>Enqueues a keyed Task result-producing operation.</summary>
    public static Task<TResult> Enqueue<TKey, TResult>(this ITaskScheduler scheduler, TKey key, Func<TKey, CancellationToken, Task<TResult>> taskFunc) where TKey : notnull
        => scheduler.Enqueue<TKey, TResult>(key, taskFunc, CancellationToken.None);

    /// <summary>Enqueues a keyed ValueTask operation.</summary>
    public static Task Enqueue<TKey>(this ITaskScheduler scheduler, TKey key, Func<TKey, ValueTask> taskFunc, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return scheduler.Enqueue<TKey, object?, Unit>(key, async ValueTask<Unit> (TKey k, object? _, CancellationToken token) => { await taskFunc(k).ConfigureAwait(true); return default(Unit); }, null, cancellationToken);
    }

    /// <summary>Enqueues a keyed ValueTask operation.</summary>
    public static Task Enqueue<TKey>(this ITaskScheduler scheduler, TKey key, Func<TKey, ValueTask> taskFunc) where TKey : notnull
        => scheduler.Enqueue<TKey>(key, taskFunc, CancellationToken.None);

    /// <summary>Enqueues a keyed ValueTask operation.</summary>
    public static Task Enqueue<TKey>(this ITaskScheduler scheduler, TKey key, Func<TKey, CancellationToken, ValueTask> taskFunc, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return scheduler.Enqueue<TKey, object?, Unit>(key, async ValueTask<Unit> (TKey k, object? _, CancellationToken token) => { await taskFunc(k, token).ConfigureAwait(true); return default(Unit); }, null, cancellationToken);
    }

    /// <summary>Enqueues a keyed ValueTask operation.</summary>
    public static Task Enqueue<TKey>(this ITaskScheduler scheduler, TKey key, Func<TKey, CancellationToken, ValueTask> taskFunc) where TKey : notnull
        => scheduler.Enqueue<TKey>(key, taskFunc, CancellationToken.None);

    /// <summary>Enqueues a keyed ValueTask result-producing operation.</summary>
    public static Task<TResult> Enqueue<TKey, TResult>(this ITaskScheduler scheduler, TKey key, Func<TKey, ValueTask<TResult>> taskFunc, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return scheduler.Enqueue<TKey, object?, TResult>(key, (TKey k, object? _, CancellationToken token) => { return taskFunc(k); }, null, cancellationToken);
    }

    /// <summary>Enqueues a keyed ValueTask result-producing operation.</summary>
    public static Task<TResult> Enqueue<TKey, TResult>(this ITaskScheduler scheduler, TKey key, Func<TKey, ValueTask<TResult>> taskFunc) where TKey : notnull
        => scheduler.Enqueue<TKey, TResult>(key, taskFunc, CancellationToken.None);

    /// <summary>Enqueues a keyed ValueTask result-producing operation.</summary>
    public static Task<TResult> Enqueue<TKey, TResult>(this ITaskScheduler scheduler, TKey key, Func<TKey, CancellationToken, ValueTask<TResult>> taskFunc, CancellationToken cancellationToken) where TKey : notnull
    {
        Argument.NotNull(taskFunc);
        return scheduler.Enqueue<TKey, object?, TResult>(key, (TKey k, object? _, CancellationToken token) => { return taskFunc(k, token); }, null, cancellationToken);
    }

    /// <summary>Enqueues a keyed ValueTask result-producing operation.</summary>
    public static Task<TResult> Enqueue<TKey, TResult>(this ITaskScheduler scheduler, TKey key, Func<TKey, CancellationToken, ValueTask<TResult>> taskFunc) where TKey : notnull
        => scheduler.Enqueue<TKey, TResult>(key, taskFunc, CancellationToken.None);
}