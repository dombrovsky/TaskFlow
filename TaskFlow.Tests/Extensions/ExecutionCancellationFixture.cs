namespace TaskFlow.Tests.Extensions;

using NUnit.Framework;
using System.Threading.Tasks.Flow;

[TestFixture]
internal sealed class ExecutionCancellationFixture
{
    private static readonly int[] ExpectedOrder = [1, 2];
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private static TaskCompletionSource<bool> Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [TestCase(1, false)]
    [TestCase(3, false)]
    [TestCase(3, true)]
    public async Task ShutdownOrCallerCancellation_ReachesMiddlewareAndDelegate_AndFinishesDisposal(int stageCount, bool cancelCaller)
    {
        foreach (var flow in TaskFlows.CreateTaskFlows())
        {
            var started = Gate();
            var release = Gate();
            using var caller = new CancellationTokenSource();
            var callerToken = cancelCaller ? caller.Token : CancellationToken.None;
            var observers = Enumerable.Range(0, stageCount).Select(_ => new TokenObserver()).ToArray();
            ITaskScheduler scheduler = flow;
            foreach (var observer in observers)
                scheduler = scheduler.UseMiddleware(observer);
            var delegateToken = CancellationToken.None;
            var operation = scheduler.Enqueue<int>(async (_, token) =>
            {
                delegateToken = token;
                started.SetResult(true);
                await release.Task.WaitAsync(token);
                return 42;
            }, null, callerToken);

            try
            {
                await started.Task.WaitAsync(TestTimeout);
                if (cancelCaller)
                    await caller.CancelAsync();
                var disposal = flow.DisposeAsync().AsTask();
                await disposal.WaitAsync(TestTimeout);
                Assert.That(async () => await operation, Throws.InstanceOf<OperationCanceledException>());
                Assert.That(delegateToken.IsCancellationRequested, Is.True);
                foreach (var observer in observers)
                {
                    Assert.That(observer.ExecutionContext!.CancellationToken, Is.EqualTo(delegateToken));
                    Assert.That(observer.ExecutionContext.CallerCancellationToken, Is.EqualTo(callerToken));
                    Assert.That(observer.ExecutionContext.ProducerCancellationToken, Is.EqualTo(callerToken));
                    Assert.That(observer.CompletionContext!.CancellationToken, Is.EqualTo(callerToken));
                    Assert.That(observer.CompletionException, Is.InstanceOf<OperationCanceledException>());
                    Assert.That(observer.CompletionCount, Is.EqualTo(1));
                    Assert.That(observer.StateShared, Is.True);
                }
            }
            finally
            {
                release.TrySetResult(true);
                await flow.DisposeAsync();
            }
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task EnqueueTokenTransformation_PreservesCallerProducerAndExecutionDistinctions(bool dropProducerToken)
    {
        foreach (var flow in TaskFlows.CreateTaskFlows())
        {
            using var caller = new CancellationTokenSource();
            using var producer = new CancellationTokenSource();
            var producerToken = dropProducerToken ? CancellationToken.None : producer.Token;
            var first = new TokenObserver();
            var second = new TokenObserver();
            var scheduler = flow.UseMiddleware(first).UseMiddleware(second)
                .UseMiddleware(new ReplaceProducerToken(producerToken));
            var started = Gate();
            var release = Gate();
            var delegateToken = CancellationToken.None;
            var operation = scheduler.Enqueue<int>(async (_, token) =>
            {
                delegateToken = token;
                started.SetResult(true);
                await release.Task.WaitAsync(token);
                return 42;
            }, null, caller.Token);

            try
            {
                await started.Task.WaitAsync(TestTimeout);
                await caller.CancelAsync();
                Assert.That(delegateToken.IsCancellationRequested, Is.False);
                Assert.That(operation.IsCompleted, Is.False);
                foreach (var observer in new[] { first, second })
                {
                    Assert.That(observer.ExecutionContext!.CallerCancellationToken, Is.EqualTo(caller.Token));
                    Assert.That(observer.ExecutionContext.ProducerCancellationToken, Is.EqualTo(producerToken));
                    Assert.That(observer.ExecutionContext.CancellationToken, Is.EqualTo(delegateToken));
                    Assert.That(delegateToken, Is.Not.EqualTo(producerToken));
                    Assert.That(delegateToken, Is.Not.EqualTo(caller.Token));
                }

                if (dropProducerToken)
                    await flow.DisposeAsync().AsTask().WaitAsync(TestTimeout);
                else
                    await producer.CancelAsync();
                Assert.That(async () => await operation.WaitAsync(TestTimeout), Throws.InstanceOf<OperationCanceledException>());
                foreach (var observer in new[] { first, second })
                {
                    Assert.That(observer.CompletionContext!.CancellationToken, Is.EqualTo(producerToken));
                    Assert.That(observer.CompletionContext.ProducerCancellationToken, Is.EqualTo(producerToken));
                    Assert.That(observer.CompletionContext.CallerCancellationToken, Is.EqualTo(caller.Token));
                }
            }
            finally
            {
                release.TrySetResult(true);
                await flow.DisposeAsync();
            }
        }
    }

    [Test]
    public async Task TerminalReplacementToken_ReachesAllExecutionStagesAndOperation_CompletionKeepsProducerToken()
    {
        using var caller = new CancellationTokenSource();
        using var producer = new CancellationTokenSource();
        using var effective = new CancellationTokenSource();
        var first = new TokenObserver();
        var second = new TokenObserver();
        var terminal = new ReplacementTokenTerminal(effective.Token);
        var scheduler = terminal.UseMiddleware(first).UseMiddleware(second)
            .UseMiddleware(new ReplaceProducerToken(producer.Token));

        var result = await scheduler.Enqueue<int>(async (_, token) =>
        {
            Assert.That(token, Is.EqualTo(effective.Token));
            await effective.CancelAsync();
            return 42;
        }, null, caller.Token);

        Assert.That(result, Is.EqualTo(42));
        Assert.That(terminal.ProducerToken, Is.EqualTo(producer.Token));
        foreach (var observer in new[] { first, second })
        {
            Assert.That(observer.ExecutionContext!.CancellationToken, Is.EqualTo(effective.Token));
            Assert.That(observer.ExecutionContext.ProducerCancellationToken, Is.EqualTo(producer.Token));
            Assert.That(observer.ExecutionContext.CallerCancellationToken, Is.EqualTo(caller.Token));
            Assert.That(observer.CompletionContext!.CancellationToken, Is.EqualTo(producer.Token));
            Assert.That(observer.CompletionContext.CancellationToken.IsCancellationRequested, Is.False);
            Assert.That(observer.CompletionCount, Is.EqualTo(1));
            Assert.That(observer.CompletionException, Is.Null);
            Assert.That(observer.StateShared, Is.True);
        }
    }

    [Test]
    public void RejectionBeforeTerminal_CompletionKeepsOriginalCallerToken()
    {
        using var caller = new CancellationTokenSource();
        var observer = new TokenObserver();
        var failure = new InvalidOperationException("rejected");
        var scheduler = new ReplacementTokenTerminal(CancellationToken.None)
            .UseMiddleware(observer).UseMiddleware(new RejectEnqueue(failure));

        var thrown = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await scheduler.Enqueue<int>((_, _) => new ValueTask<int>(42), null, caller.Token));

        Assert.That(thrown, Is.SameAs(failure));
        Assert.That(observer.ExecutionContext, Is.Null);
        Assert.That(observer.CompletionContext!.CancellationToken, Is.EqualTo(caller.Token));
        Assert.That(observer.CompletionContext.ProducerCancellationToken, Is.EqualTo(caller.Token));
        Assert.That(observer.CompletionException, Is.SameAs(failure));
        Assert.That(observer.CompletionCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Shutdown_WaitsForDelegateThatIgnoresCancellation_AndDrainsQueueInOrder()
    {
        foreach (var flow in TaskFlows.CreateTaskFlows())
        {
            var observer = new TokenObserver();
            var scheduler = flow.UseMiddleware(observer);
            var started = Gate();
            var release = Gate();
            var order = new List<int>();
            var first = scheduler.Enqueue<int>(async (_, _) =>
            {
                started.SetResult(true);
                await release.Task;
                order.Add(1);
                return 1;
            }, null, CancellationToken.None);

            try
            {
                await started.Task.WaitAsync(TestTimeout);
                var second = scheduler.Enqueue<int>((_, token) =>
                {
                    Assert.That(token.IsCancellationRequested, Is.True);
                    order.Add(2);
                    return new ValueTask<int>(2);
                }, null, CancellationToken.None);
                var disposal = flow.DisposeAsync().AsTask();
                Assert.That(disposal.IsCompleted, Is.False);
                Assert.That(second.IsCompleted, Is.False);
                release.SetResult(true);
                await disposal.WaitAsync(TestTimeout);
                Assert.That(await first, Is.EqualTo(1));
                Assert.That(await second, Is.EqualTo(2));
                Assert.That(order, Is.EqualTo(ExpectedOrder));
                Assert.That(observer.CompletionCount, Is.EqualTo(2));
            }
            finally
            {
                release.TrySetResult(true);
                await flow.DisposeAsync();
            }
        }
    }

    private sealed class TokenObserver : ITaskSchedulerExecutionMiddleware, ITaskSchedulerCompletionMiddleware
    {
        public TaskSchedulerOperationContext? ExecutionContext { get; private set; }
        public TaskSchedulerOperationContext? CompletionContext { get; private set; }
        public Exception? CompletionException { get; private set; }
        public int CompletionCount { get; private set; }
        public bool StateShared { get; private set; }

        public ValueTask<TResult> InvokeAsync<TResult>(TaskSchedulerOperationContext context, TaskSchedulerExecutionDelegate<TResult> continuation)
        {
            ExecutionContext = context;
            context.GetOrCreateLocalState(() => new object());
            return continuation(context);
        }

        public ValueTask<TaskSchedulerOperationOutcome<TResult>> InvokeAsync<TResult>(TaskSchedulerOperationContext context, TaskSchedulerOperationOutcome<TResult> outcome, TaskSchedulerCompletionDelegate<TResult> continuation)
        {
            CompletionContext = context;
            CompletionException = outcome.Exception;
            CompletionCount++;
            StateShared = ReferenceEquals(context.GetLocalState<object>(), ExecutionContext?.GetLocalState<object>());
            return continuation(context, outcome);
        }
    }

    private sealed class ReplaceProducerToken(CancellationToken token) : ITaskSchedulerEnqueueMiddleware
    {
        public Task<TResult> InvokeAsync<TResult>(TaskSchedulerEnqueueContext<TResult> context, TaskSchedulerEnqueueDelegate<TResult> continuation)
            => continuation(context.WithCancellationToken(token));
    }

    private sealed class RejectEnqueue(Exception exception) : ITaskSchedulerEnqueueMiddleware
    {
        public Task<TResult> InvokeAsync<TResult>(TaskSchedulerEnqueueContext<TResult> context, TaskSchedulerEnqueueDelegate<TResult> continuation)
            => Task.FromException<TResult>(exception);
    }

    private sealed class ReplacementTokenTerminal(CancellationToken token) : ITaskScheduler
    {
        public CancellationToken ProducerToken { get; private set; }

        public async Task<TResult> Enqueue<TResult>(Func<object?, CancellationToken, ValueTask<TResult>> taskFunc, object? state, CancellationToken cancellationToken)
        {
            ProducerToken = cancellationToken;
            return await taskFunc(state, token);
        }
    }
}