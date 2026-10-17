namespace TaskFlow.Tests;

using NUnit.Framework;
using System.Threading.Tasks.Flow;

[TestFixture]
[CancelAfter(10000)]
internal sealed class ThreadTaskFlowDisposalFixture
{
    private static readonly int[] ExpectedExecutionOrder = [1, 4, 2, 3];
    private static readonly string[] RejectedOverloads =
    [
        "DirectGeneric", "Action", "ActionWithToken", "TokenAction", "TokenActionWithToken",
        "Task", "TokenTask", "TokenTaskWithToken", "StateTask",
        "ValueTask", "TokenValueTask", "TokenValueTaskWithToken", "StateValueTask",
    ];

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task DisposeAsync_ShouldSettleCallerTasksBeforeReturning(bool currentThread, bool asynchronous)
    {
        await using var sut = CreateFlow(currentThread);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lastStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lastRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var failure = new InvalidOperationException("Queued failure");
        var executed = new List<int>();
        var observedCancellation = CancellationToken.None;
        var first = sut.Enqueue(async _ =>
        {
            started.SetResult();
            await release.Task;
        });

        Task<int> Result(object? state, CancellationToken token) => RunAsync(1, token);
        Task Fault(CancellationToken token) => RunAsync(2, token);
        async ValueTask Canceled(CancellationToken token)
        {
            observedCancellation = token;
            await RunAsync(3, token);
        }
        async Task<int> RunAsync(int value, CancellationToken token)
        {
            if (asynchronous)
                await Task.Yield();
            executed.Add(value);
            if (value == 2)
                throw failure;
            if (value == 3)
                token.ThrowIfCancellationRequested();
            return value;
        }

        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var result = sut.Enqueue((state, token) => new ValueTask<int>(Result(state, token)), null, CancellationToken.None);
            var action = sut.Enqueue(() => executed.Add(4));
            var fault = sut.Enqueue(Fault);
            var canceled = sut.Enqueue(Canceled, cancellation.Token, null);
            var last = sut.Enqueue(async _ =>
            {
                lastStarted.SetResult();
                await lastRelease.Task;
            });
            await cancellation.CancelAsync();

            var disposal = sut.DisposeAsync().AsTask();
            Assert.That(disposal.IsCompleted, Is.False);
            release.SetResult();
            await lastStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(disposal.IsCompleted, Is.False, "Disposal must wait for the last asynchronous operation.");
            lastRelease.SetResult();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(first.IsCompletedSuccessfully, Is.True);
                Assert.That(result.IsCompletedSuccessfully, Is.True);
                Assert.That(action.IsCompletedSuccessfully, Is.True);
                Assert.That(fault.IsFaulted, Is.True);
                Assert.That(canceled.IsCanceled, Is.True);
                Assert.That(last.IsCompletedSuccessfully, Is.True);
                Assert.That(executed, Is.EqualTo(ExpectedExecutionOrder));
            }
            Assert.That(await result, Is.EqualTo(1));
            await Assert.ThatAsync(async () => await fault, Throws.Exception.SameAs(failure));
            await Assert.ThatAsync(async () => await canceled,
                Throws.InstanceOf<OperationCanceledException>().With.Property("CancellationToken").EqualTo(observedCancellation));
        }
        finally
        {
            release.TrySetResult();
            lastRelease.TrySetResult();
        }
    }

    [Test]
    public void Enqueue_AfterDispose_ShouldThrowSynchronously(
        [Values(false, true)] bool currentThread,
        [ValueSource(nameof(RejectedOverloads))] string overload)
    {
        using var sut = CreateFlow(currentThread);
        sut.Dispose();
        AssertDisposedEnqueueThrows(sut, overload);
    }

    [Test]
    public async Task Enqueue_AfterDisposeAsync_ShouldThrowSynchronously(
        [Values(false, true)] bool currentThread,
        [ValueSource(nameof(RejectedOverloads))] string overload)
    {
        await using var sut = CreateFlow(currentThread);
        await sut.DisposeAsync();
        AssertDisposedEnqueueThrows(sut, overload);
    }

    [Test]
    public async Task Enqueue_AcceptedOperationThrowsObjectDisposed_ShouldFaultReturnedTask(
        [Values(false, true)] bool currentThread,
        [Values(false, true)] bool convenienceOverload)
    {
        await using var sut = CreateFlow(currentThread);
        var failure = new ObjectDisposedException("operation-resource");
        Task? task = null;
        Assert.That(() =>
        {
            task = convenienceOverload
                ? sut.Enqueue((Action)(() => throw failure))
                : sut.Enqueue<int>((state, token) => throw failure, null, CancellationToken.None);
        }, Throws.Nothing);
        await Assert.ThatAsync(async () => await task!, Throws.Exception.SameAs(failure));
        Assert.That(task!.IsFaulted, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Enqueue_NullDelegate_ShouldReturnFaultedTask(bool currentThread)
    {
        await using var sut = CreateFlow(currentThread);
        Task<int>? task = null;
        Assert.That(() => { task = sut.Enqueue<int>(null!, null, CancellationToken.None); }, Throws.Nothing);
        Assert.That(task!.IsFaulted, Is.True);
        await Assert.ThatAsync(async () => await task, Throws.TypeOf<ArgumentNullException>());
    }

    private static void AssertDisposedEnqueueThrows(ThreadTaskFlow sut, string overload)
    {
        var invoked = false;
        Task? returnedTask = null;
        void Action() => invoked = true;
        Task TaskOperation()
        { Action(); return Task.CompletedTask; }
        ValueTask ValueTaskOperation()
        { Action(); return ValueTask.CompletedTask; }

        Assert.That(() =>
        {
            returnedTask = overload switch
            {
                "DirectGeneric" => sut.Enqueue((state, token) => { Action(); return new ValueTask<int>(42); }, null, CancellationToken.None),
                "Action" => sut.Enqueue(Action),
                "ActionWithToken" => sut.Enqueue(Action, CancellationToken.None),
                "TokenAction" => sut.Enqueue(token => { Action(); }),
                "TokenActionWithToken" => sut.Enqueue(token => { Action(); }, CancellationToken.None),
                "Task" => sut.Enqueue(TaskOperation),
                "TokenTask" => sut.Enqueue(token => TaskOperation()),
                "TokenTaskWithToken" => sut.Enqueue(token => TaskOperation(), CancellationToken.None),
                "StateTask" => sut.Enqueue((int state, CancellationToken token) => TaskOperation(), 42, CancellationToken.None),
                "ValueTask" => sut.Enqueue(ValueTaskOperation, null),
                "TokenValueTask" => sut.Enqueue(token => ValueTaskOperation(), null),
                "TokenValueTaskWithToken" => sut.Enqueue(token => ValueTaskOperation(), CancellationToken.None, null),
                "StateValueTask" => sut.Enqueue((int state, CancellationToken token) => ValueTaskOperation(), 42, CancellationToken.None),
                _ => throw new ArgumentOutOfRangeException(nameof(overload)),
            };
        }, Throws.TypeOf<ObjectDisposedException>());
        Assert.That(returnedTask, Is.Null, "A disposed flow must reject the call before returning a task.");
        Assert.That(invoked, Is.False, "A rejected delegate must never execute.");
    }

    private static ThreadTaskFlow CreateFlow(bool currentThread)
    {
        if (!currentThread)
            return new DedicatedThreadTaskFlow();
        var flow = new CurrentThreadTaskFlow();
        new Thread(flow.Run) { IsBackground = true }.Start();
        return flow;
    }
}