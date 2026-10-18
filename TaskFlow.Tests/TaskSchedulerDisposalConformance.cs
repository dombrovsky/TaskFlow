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
            _ => throw new ArgumentOutOfRangeException(nameof(overload)),
        };
    }
}