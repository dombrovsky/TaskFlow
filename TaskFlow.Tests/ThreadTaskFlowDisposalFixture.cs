namespace TaskFlow.Tests;

using NUnit.Framework;
using System.Threading.Tasks.Flow;

[TestFixture]
[CancelAfter(10000)]
internal sealed class ThreadTaskFlowDisposalFixture
{
    private static readonly int[] ExpectedExecutionOrder = [1, 4, 2, 3];

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

    [TestCase(false)]
    [TestCase(true)]
    public async Task Enqueue_AfterDisposal_ShouldReturnFaultedTask(bool currentThread)
    {
        await using var sut = CreateFlow(currentThread);
        await sut.DisposeAsync();
        Task<int>? task = null;
        Assert.That(() => { task = sut.Enqueue((state, token) => new ValueTask<int>(42), null, CancellationToken.None); }, Throws.Nothing);
        Assert.That(task!.IsFaulted, Is.True);
        await Assert.ThatAsync(async () => await task, Throws.TypeOf<ObjectDisposedException>());
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

    private static ThreadTaskFlow CreateFlow(bool currentThread)
    {
        if (!currentThread)
            return new DedicatedThreadTaskFlow();
        var flow = new CurrentThreadTaskFlow();
        new Thread(flow.Run) { IsBackground = true }.Start();
        return flow;
    }
}