namespace TaskFlow.Tests.Extensions;

using NUnit.Framework;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks.Flow;

[TestFixture]
[CancelAfter(10000)]
[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The middleware factory transfers ownership to the flow.")]
internal sealed class CallerWaitOwnershipFixture
{
    private static readonly string[] CleanupOrder = ["delegate:finished", "completion", "cleanup"];

    private static TaskCompletionSource<bool> Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static IEnumerable<TestCaseData> NestedCases()
    {
        foreach (var outcome in new[] { "success", "fault", "cancel" })
            foreach (var outerFirst in new[] { false, true })
                foreach (var innerWaitFails in new[] { false, true })
                    yield return new TestCaseData(outcome, outerFirst, innerWaitFails);
    }

    [TestCaseSource(nameof(NestedCases))]
    public async Task NestedBoundaries_CallerCompletionDoesNotSignalProducerTermination(
        string producerOutcome, bool outerFirst, bool innerWaitFails)
    {
        await using var flow = new TaskFlow();
        var events = new ConcurrentQueue<string>();
        var resources = new CompletionResources(events);
        var controller = new BoundedAdmissionController(new BoundedAdmissionOptions
        { Capacity = 1, CapacityMode = TaskSchedulerCapacityMode.Outstanding });
        var bounded = flow.WithBoundedAdmission(controller);
        var innerFailure = new FormatException("inner caller wait");
        var inner = new CallerWait(innerWaitFails ? innerFailure : null);
        var outer = new CallerWait(null);
        var scheduler = bounded.UseMiddleware(new Factory(resources)).UseMiddleware(inner).UseMiddleware(outer);
        var entered = Gate();
        var release = Gate();
        var producerFailure = new InvalidOperationException("late producer failure");
        var finished = false;
        var caller = scheduler.Enqueue<int>(async (_, _) =>
        {
            entered.SetResult(true);
            try
            {
                await release.Task; // Deliberately ignore caller/owner cancellation until released.
                if (producerOutcome == "fault")
                    throw producerFailure;
                if (producerOutcome == "cancel")
                    throw new OperationCanceledException(new CancellationToken(true));
                return 42;
            }
            finally
            {
                finished = true;
                events.Enqueue("delegate:finished");
            }
        }, null, default);
        try
        {
            await Task.WhenAll(entered.Task, inner.Started.Task, outer.Started.Task);
            if (outerFirst)
            {
                outer.Release.SetResult(true);
                Assert.That(await caller, Is.EqualTo(17));
                Assert.That(outer.Downstream!.IsCompleted, Is.False);
            }
            inner.Release.SetResult(true);
            var downstreamFailure = await outer.Observed!;
            Assert.That(downstreamFailure, innerWaitFails ? Is.SameAs(innerFailure) : Is.Null);
            Assert.That(outer.Downstream!.IsCompleted, Is.True);
            Assert.That(inner.Downstream!.IsCompleted, Is.False);
            Assert.That(finished, Is.False);
            if (!outerFirst)
            {
                Assert.That(caller.IsCompleted, Is.False);
                outer.Release.SetResult(true);
                Assert.That(await caller, Is.EqualTo(17));
            }

            var rejected = controller.TrySubmit(bounded, _ => Task.FromResult(43));
            Assert.That(rejected.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Full));
            Assert.That(rejected.Completion, Is.Null);
            var shutdown = flow.DisposeAsync().AsTask();
            Assert.That(shutdown.IsCompleted, Is.False);
            Assert.That(resources.Cleanups, Is.Zero);
            Assert.That(events, Is.Empty);
            release.SetResult(true);
            var lateFailure = await inner.Observed!;
            if (producerOutcome == "fault")
                Assert.That(lateFailure, Is.SameAs(producerFailure));
            else if (producerOutcome == "cancel")
                Assert.That(lateFailure, Is.InstanceOf<OperationCanceledException>());
            else
                Assert.That(lateFailure, Is.Null);
            await shutdown;
            await flow.DisposeAsync();
            Assert.That(finished, Is.True);
            Assert.That(resources.Completions, Is.EqualTo(1));
            Assert.That(resources.Cleanups, Is.EqualTo(1));
            Assert.That(events.ToArray(), Is.EqualTo(CleanupOrder));
            if (producerOutcome == "fault")
                Assert.That(resources.Failure, Is.SameAs(producerFailure));
        }
        finally
        {
            outer.Release.TrySetResult(true);
            inner.Release.TrySetResult(true);
            release.TrySetResult(true);
            await flow.DisposeAsync();
        }
    }

    private sealed class CallerWait(Exception? failure) : ITaskSchedulerEnqueueMiddleware
    {
        public TaskCompletionSource<bool> Started { get; } = Gate();
        public TaskCompletionSource<bool> Release { get; } = Gate();
        public Task? Downstream { get; private set; }
        public Task<Exception?>? Observed { get; private set; }

        public Task<T> InvokeAsync<T>(TaskSchedulerEnqueueContext<T> context, TaskSchedulerEnqueueDelegate<T> next)
            => context.InvokeWithCallerWait(next, owned =>
            {
                Downstream = owned(context);
                Observed = Observe(Downstream);
                Started.TrySetResult(true);
                return Wait<T>();
            });

        private async Task<T> Wait<T>()
        {
            await Release.Task;
            if (failure != null)
                throw failure;
            return (T)(object)17;
        }

        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Observe all late producer failures after the caller has stopped waiting.")]
        private static async Task<Exception?> Observe(Task pending)
        {
            try
            { await pending; return null; }
            catch (Exception exception) { return exception; }
        }
    }

    private sealed class Factory(ITaskSchedulerMiddleware middleware) : ITaskSchedulerMiddlewareFactory
    {
        public ITaskSchedulerMiddleware Create(TaskSchedulerMiddlewareLifetime lifetime) => middleware;
    }

    private sealed class CompletionResources(ConcurrentQueue<string> events) : ITaskSchedulerCompletionMiddleware, IDisposable
    {
        public int Completions { get; private set; }
        public int Cleanups { get; private set; }
        public Exception? Failure { get; private set; }

        public ValueTask<TaskSchedulerOperationOutcome<T>> InvokeAsync<T>(TaskSchedulerOperationContext context,
            TaskSchedulerOperationOutcome<T> outcome, TaskSchedulerCompletionDelegate<T> next)
        {
            Completions++;
            Failure = outcome.Exception;
            events.Enqueue("completion");
            return next(context, outcome);
        }

        public void Dispose()
        {
            Cleanups++;
            events.Enqueue("cleanup");
        }
    }
}