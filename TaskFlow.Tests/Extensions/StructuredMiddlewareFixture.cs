namespace TaskFlow.Tests.Extensions;

using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks.Flow;

[TestFixture]
[CancelAfter(10000)]
[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Middleware factories transfer ownership to the flow.")]
internal sealed class StructuredMiddlewareFixture
{
    private static TaskCompletionSource<bool> Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [TestCase("success", false)]
    [TestCase("success", true)]
    [TestCase("fault", false)]
    [TestCase("fault", true)]
    [TestCase("cancel", false)]
    [TestCase("cancel", true)]
    public async Task ConcurrentAttempts_JoinEveryAttemptBeforeCleanup(string outcome, bool synchronous)
    {
        await using var flow = new TaskFlow();
        var middleware = new JoinedAttempts();
        var scheduler = flow.UseMiddleware(new Factory(middleware));
        var entered = Gate();
        var release = Gate();
        var calls = 0;
        var failure = new FormatException("attempt");
        var pending = scheduler.Enqueue<int>(Attempt, null, default);

        ValueTask<int> Attempt(object? state, CancellationToken token)
        {
            var call = Interlocked.Increment(ref calls);
            if (synchronous && call == 1 && outcome == "fault")
                throw failure;
            if (synchronous && call == 1 && outcome == "cancel")
                throw new OperationCanceledException(new CancellationToken(true));
            return Finish(call);
        }

        async ValueTask<int> Finish(int call)
        {
            if (!synchronous)
                await Task.Yield();
            if (call == 1 && outcome == "fault")
                throw failure;
            if (call == 1 && outcome == "cancel")
                throw new OperationCanceledException(new CancellationToken(true));
            if (call == 2)
            {
                entered.SetResult(true);
                await release.Task;
            }
            return 42;
        }
        try
        {
            await entered.Task;
            var shutdown = flow.DisposeAsync().AsTask();
            Assert.That(pending.IsCompleted, Is.False);
            Assert.That(shutdown.IsCompleted, Is.False);
            Assert.That(middleware.Cleaned, Is.False);
            release.SetResult(true);
            if (outcome == "success")
                Assert.That(await pending, Is.EqualTo(42));
            else if (outcome == "fault")
                await Assert.ThatAsync(async () => await pending, Throws.Exception.SameAs(failure));
            else
                await Assert.ThatAsync(async () => await pending, Throws.InstanceOf<OperationCanceledException>());
            await shutdown;
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(middleware.Cleaned, Is.True);
        }
        finally { release.TrySetResult(true); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task TimeoutBoundary_RetainsAdmissionAndResourcesUntilActualTermination(bool delayedAdmission)
    {
        await using var flow = new TaskFlow();
        var clock = new FakeTimeProvider();
        var resources = new JoinedAttempts { Attempts = 1 };
        var admission = new BoundedAdmissionController(new BoundedAdmissionOptions { Capacity = 1 });
        var delay = new DelayedAdmission();
        var bounded = flow.WithBoundedAdmission(admission).UseMiddleware(new Factory(resources));
        ITaskScheduler producer = delayedAdmission ? bounded.UseMiddleware(delay) : bounded;
        var scheduler = producer.WithTimeout(TimeSpan.FromSeconds(10), clock);
        var entered = Gate();
        var release = Gate();
        var pending = scheduler.Enqueue<int>(async (_, token) =>
        {
            entered.SetResult(true);
            await release.Task; // Deliberately ignore cancellation; ownership must follow actual termination.
            return 42;
        }, null, default);
        try
        {
            if (delayedAdmission)
            {
                await delay.Entered.Task;
                delay.Release.SetResult(true);
            }
            await entered.Task;
            clock.Advance(TimeSpan.FromSeconds(10));
            await Assert.ThatAsync(async () => await pending, Throws.TypeOf<TimeoutException>());
            var rejected = admission.TrySubmit(bounded, _ => Task.FromResult(43));
            Assert.That(rejected.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Full));
            var shutdown = flow.DisposeAsync().AsTask();
            Assert.That(shutdown.IsCompleted, Is.False);
            Assert.That(resources.Cleaned, Is.False);
            release.SetResult(true);
            await shutdown;
            Assert.That(resources.Cleaned, Is.True);
        }
        finally { release.TrySetResult(true); delay.Release.TrySetResult(true); }
    }

    private sealed class Factory(ITaskSchedulerMiddleware middleware) : ITaskSchedulerMiddlewareFactory
    {
        public ITaskSchedulerMiddleware Create(TaskSchedulerMiddlewareLifetime lifetime) => middleware;
    }

    private sealed class JoinedAttempts : ITaskSchedulerExecutionMiddleware, IDisposable
    {
        public int Attempts { get; init; } = 2;
        public bool Cleaned { get; private set; }

        public async ValueTask<T> InvokeAsync<T>(TaskSchedulerOperationContext context, TaskSchedulerExecutionDelegate<T> next)
        {
            var attempts = new Task<T>[Attempts];
            for (var i = 0; i < attempts.Length; i++)
                attempts[i] = InvokeAttempt();
            var results = await Task.WhenAll(attempts).ConfigureAwait(true);
            return results[0];

            async Task<T> InvokeAttempt() => await next(context).ConfigureAwait(true);
        }

        public void Dispose() => Cleaned = true;
    }

    private sealed class DelayedAdmission : ITaskSchedulerEnqueueMiddleware
    {
        public TaskCompletionSource<bool> Entered { get; } = Gate();
        public TaskCompletionSource<bool> Release { get; } = Gate();

        public async Task<T> InvokeAsync<T>(TaskSchedulerEnqueueContext<T> context, TaskSchedulerEnqueueDelegate<T> next)
        {
            Entered.SetResult(true);
            await Release.Task;
            return await next(context);
        }
    }
}