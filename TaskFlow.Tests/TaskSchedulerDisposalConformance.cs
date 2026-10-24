namespace TaskFlow.Tests;

using NUnit.Framework;
using System.Threading.Tasks.Flow;

internal static class TaskSchedulerDisposalConformance
{
    public static readonly string[] Overloads =
    [
        "DirectGeneric", "Action", "ActionWithToken", "TokenAction", "TokenActionWithToken",
        "Task", "TokenTask", "TokenTaskWithToken", "StateTask",
        "ValueTask", "TokenValueTask", "TokenValueTaskWithToken", "StateValueTask",
        "TaskResult", "TokenTaskResult", "TokenTaskResultWithToken", "StateTaskResult",
        "ValueTaskResult", "TokenValueTaskResult", "TokenValueTaskResultWithToken", "StateValueTaskResult",
        "Func", "TokenFunc",
    ];

    public static void AssertRejected(ITaskScheduler scheduler, string overload, bool keyed)
    {
        var invoked = false;
        Task? returnedTask = null;
        Assert.That(() => { returnedTask = Enqueue(scheduler, overload, () => invoked = true, keyed); }, Throws.TypeOf<ObjectDisposedException>());
        Assert.That(returnedTask, Is.Null, "A disposed scheduler must reject the call before returning a task.");
        Assert.That(invoked, Is.False, "A rejected delegate must never execute.");
    }

    public static Task Enqueue(ITaskScheduler scheduler, string overload, Action action, bool keyed)
    {
        Task TaskOperation()
        { action(); return Task.CompletedTask; }
        ValueTask ValueTaskOperation()
        { action(); return ValueTask.CompletedTask; }
        if (keyed)
        {
            return overload switch
            {
                "DirectGeneric" => scheduler.Enqueue<string, int, int>("key", (key, state, token) => { action(); return new ValueTask<int>(42); }, 42, CancellationToken.None),
                "Action" => scheduler.Enqueue<string>("key", action),
                "ActionWithToken" => scheduler.Enqueue<string>("key", action, CancellationToken.None),
                "TokenAction" => scheduler.Enqueue<string>("key", (string key) => { action(); }),
                "TokenActionWithToken" => scheduler.Enqueue<string>("key", (string key) => { action(); }, CancellationToken.None),
                "Task" => scheduler.Enqueue<string>("key", TaskOperation),
                "TokenTask" => scheduler.Enqueue<string>("key", (CancellationToken token) => TaskOperation()),
                "TokenTaskWithToken" => scheduler.Enqueue<string>("key", (CancellationToken token) => TaskOperation(), CancellationToken.None),
                "StateTask" => scheduler.Enqueue<string, int, int>("key", (key, state, token) => { action(); return Task.FromResult(state); }, 42, CancellationToken.None),
                "ValueTask" => scheduler.Enqueue<string>("key", ValueTaskOperation),
                "TokenValueTask" => scheduler.Enqueue<string>("key", (CancellationToken token) => ValueTaskOperation()),
                "TokenValueTaskWithToken" => scheduler.Enqueue<string>("key", (CancellationToken token) => ValueTaskOperation(), CancellationToken.None),
                "StateValueTask" => scheduler.Enqueue<string, int, int>("key", (key, state, token) => { action(); return new ValueTask<int>(state); }, 42, CancellationToken.None),
                "TaskResult" => scheduler.Enqueue<string, int>("key", () => { action(); return Task.FromResult(42); }),
                "TokenTaskResult" => scheduler.Enqueue<string, int>("key", (CancellationToken token) => { action(); return Task.FromResult(42); }),
                "TokenTaskResultWithToken" => scheduler.Enqueue<string, int>("key", (CancellationToken token) => { action(); return Task.FromResult(42); }, CancellationToken.None),
                "StateTaskResult" => scheduler.Enqueue<string, int, int>("key", (key, state, token) => { action(); return Task.FromResult(state); }, 42, CancellationToken.None),
                "ValueTaskResult" => scheduler.Enqueue<string, int>("key", () => { action(); return new ValueTask<int>(42); }),
                "TokenValueTaskResult" => scheduler.Enqueue<string, int>("key", (CancellationToken token) => { action(); return new ValueTask<int>(42); }),
                "TokenValueTaskResultWithToken" => scheduler.Enqueue<string, int>("key", (CancellationToken token) => { action(); return new ValueTask<int>(42); }, CancellationToken.None),
                "StateValueTaskResult" => scheduler.Enqueue<string, int, int>("key", (key, state, token) => { action(); return new ValueTask<int>(state); }, 42, CancellationToken.None),
                "Func" => scheduler.Enqueue<string, int>("key", () => { action(); return 42; }),
                "TokenFunc" => scheduler.Enqueue<string, int>("key", (string key) => { action(); return 42; }, CancellationToken.None),
                _ => throw new ArgumentOutOfRangeException(nameof(overload)),
            };
        }

        return overload switch
        {
            "DirectGeneric" => scheduler.Enqueue((state, token) => { action(); return new ValueTask<int>(42); }, null, CancellationToken.None),
            "Action" => scheduler.Enqueue(action),
            "ActionWithToken" => scheduler.Enqueue(action, CancellationToken.None),
            "TokenAction" => scheduler.Enqueue(token => { action(); }),
            "TokenActionWithToken" => scheduler.Enqueue(token => { action(); }, CancellationToken.None),
            "Task" => scheduler.Enqueue(TaskOperation),
            "TokenTask" => scheduler.Enqueue(token => TaskOperation()),
            "TokenTaskWithToken" => scheduler.Enqueue(token => TaskOperation(), CancellationToken.None),
            "StateTask" => scheduler.Enqueue((int state, CancellationToken token) => TaskOperation(), 42, CancellationToken.None),
            "ValueTask" => scheduler.Enqueue(ValueTaskOperation, null),
            "TokenValueTask" => scheduler.Enqueue(token => ValueTaskOperation(), null),
            "TokenValueTaskWithToken" => scheduler.Enqueue(token => ValueTaskOperation(), CancellationToken.None, null),
            "StateValueTask" => scheduler.Enqueue((int state, CancellationToken token) => ValueTaskOperation(), 42, CancellationToken.None),
            "TaskResult" => scheduler.Enqueue(() => { action(); return Task.FromResult(42); }),
            "TokenTaskResult" => scheduler.Enqueue(token => { action(); return Task.FromResult(42); }),
            "TokenTaskResultWithToken" => scheduler.Enqueue(token => { action(); return Task.FromResult(42); }, CancellationToken.None),
            "StateTaskResult" => scheduler.Enqueue((int state, CancellationToken token) => { action(); return Task.FromResult(state); }, 42, CancellationToken.None),
            "ValueTaskResult" => scheduler.Enqueue(() => { action(); return new ValueTask<int>(42); }, null),
            "TokenValueTaskResult" => scheduler.Enqueue(token => { action(); return new ValueTask<int>(42); }, null),
            "TokenValueTaskResultWithToken" => scheduler.Enqueue(token => { action(); return new ValueTask<int>(42); }, CancellationToken.None),
            "StateValueTaskResult" => scheduler.Enqueue((int state, CancellationToken token) => { action(); return new ValueTask<int>(state); }, 42, CancellationToken.None),
            "Func" => scheduler.Enqueue(() => { action(); return 42; }),
            "TokenFunc" => scheduler.Enqueue(token => { action(); return 42; }, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(overload)),
        };
    }

    public static Task EnqueueNullDelegate(ITaskScheduler scheduler, string overload, bool keyed)
    {
        if (keyed)
        {
            return overload switch
            {
                "DirectGeneric" => scheduler.Enqueue<string, int, int>("key", (Func<string, int, CancellationToken, ValueTask<int>>)null!, 42, CancellationToken.None),
                "Action" => scheduler.Enqueue<string>("key", (Action)null!),
                "ActionWithToken" => scheduler.Enqueue<string>("key", (Action)null!, CancellationToken.None),
                "TokenAction" => scheduler.Enqueue<string>("key", (Action<string>)null!),
                "TokenActionWithToken" => scheduler.Enqueue<string>("key", (Action<string>)null!, CancellationToken.None),
                "Task" => scheduler.Enqueue<string>("key", (Func<Task>)null!),
                "TokenTask" => scheduler.Enqueue<string>("key", (Func<CancellationToken, Task>)null!),
                "TokenTaskWithToken" => scheduler.Enqueue<string>("key", (Func<CancellationToken, Task>)null!, CancellationToken.None),
                "StateTask" => scheduler.Enqueue<string, int, int>("key", (Func<string, int, CancellationToken, Task<int>>)null!, 42, CancellationToken.None),
                "ValueTask" => scheduler.Enqueue<string>("key", (Func<ValueTask>)null!),
                "TokenValueTask" => scheduler.Enqueue<string>("key", (Func<CancellationToken, ValueTask>)null!),
                "TokenValueTaskWithToken" => scheduler.Enqueue<string>("key", (Func<CancellationToken, ValueTask>)null!, CancellationToken.None),
                "StateValueTask" => scheduler.Enqueue<string, int, int>("key", (Func<string, int, CancellationToken, ValueTask<int>>)null!, 42, CancellationToken.None),
                "TaskResult" => scheduler.Enqueue<string, int>("key", (Func<Task<int>>)null!),
                "TokenTaskResult" => scheduler.Enqueue<string, int>("key", (Func<CancellationToken, Task<int>>)null!),
                "TokenTaskResultWithToken" => scheduler.Enqueue<string, int>("key", (Func<CancellationToken, Task<int>>)null!, CancellationToken.None),
                "StateTaskResult" => scheduler.Enqueue<string, int, int>("key", (Func<string, int, CancellationToken, Task<int>>)null!, 42, CancellationToken.None),
                "ValueTaskResult" => scheduler.Enqueue<string, int>("key", (Func<ValueTask<int>>)null!),
                "TokenValueTaskResult" => scheduler.Enqueue<string, int>("key", (Func<CancellationToken, ValueTask<int>>)null!),
                "TokenValueTaskResultWithToken" => scheduler.Enqueue<string, int>("key", (Func<CancellationToken, ValueTask<int>>)null!, CancellationToken.None),
                "StateValueTaskResult" => scheduler.Enqueue<string, int, int>("key", (Func<string, int, CancellationToken, ValueTask<int>>)null!, 42, CancellationToken.None),
                "Func" => scheduler.Enqueue<string, int>("key", (Func<int>)null!),
                "TokenFunc" => scheduler.Enqueue<string, int>("key", (Func<string, int>)null!, CancellationToken.None),
                _ => throw new ArgumentOutOfRangeException(nameof(overload)),
            };
        }
        return overload switch
        {
            "DirectGeneric" => scheduler.Enqueue<int>(null!, null, CancellationToken.None),
            "Action" => scheduler.Enqueue((Action)null!),
            "ActionWithToken" => scheduler.Enqueue((Action)null!, CancellationToken.None),
            "TokenAction" => scheduler.Enqueue((Action<CancellationToken>)null!),
            "TokenActionWithToken" => scheduler.Enqueue((Action<CancellationToken>)null!, CancellationToken.None),
            "Task" => scheduler.Enqueue((Func<Task>)null!),
            "TokenTask" => scheduler.Enqueue((Func<CancellationToken, Task>)null!),
            "TokenTaskWithToken" => scheduler.Enqueue((Func<CancellationToken, Task>)null!, CancellationToken.None),
            "StateTask" => scheduler.Enqueue((Func<int, CancellationToken, Task>)null!, 42, CancellationToken.None),
            "ValueTask" => scheduler.Enqueue((Func<ValueTask>)null!, _: null),
            "TokenValueTask" => scheduler.Enqueue((Func<CancellationToken, ValueTask>)null!, _: null),
            "TokenValueTaskWithToken" => scheduler.Enqueue((Func<CancellationToken, ValueTask>)null!, CancellationToken.None, null),
            "StateValueTask" => scheduler.Enqueue((Func<int, CancellationToken, ValueTask>)null!, 42, CancellationToken.None),
            "TaskResult" => scheduler.Enqueue((Func<Task<int>>)null!),
            "TokenTaskResult" => scheduler.Enqueue((Func<CancellationToken, Task<int>>)null!),
            "TokenTaskResultWithToken" => scheduler.Enqueue((Func<CancellationToken, Task<int>>)null!, CancellationToken.None),
            "StateTaskResult" => scheduler.Enqueue((Func<int, CancellationToken, Task<int>>)null!, 42, CancellationToken.None),
            "ValueTaskResult" => scheduler.Enqueue((Func<ValueTask<int>>)null!, _: null),
            "TokenValueTaskResult" => scheduler.Enqueue((Func<CancellationToken, ValueTask<int>>)null!, _: null),
            "TokenValueTaskResultWithToken" => scheduler.Enqueue((Func<CancellationToken, ValueTask<int>>)null!, CancellationToken.None),
            "StateValueTaskResult" => scheduler.Enqueue((Func<int, CancellationToken, ValueTask<int>>)null!, 42, CancellationToken.None),
            "Func" => scheduler.Enqueue((Func<int>)null!),
            "TokenFunc" => scheduler.Enqueue((Func<CancellationToken, int>)null!, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(overload)),
        };
    }
}