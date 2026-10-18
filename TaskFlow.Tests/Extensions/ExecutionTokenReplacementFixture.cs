namespace TaskFlow.Tests.Extensions;

using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using System.Threading.Tasks.Flow;

[TestFixture]
internal sealed class ExecutionTokenReplacementFixture
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);
    private static TaskCompletionSource<bool> Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    public async Task TerminalToken_ReachesActualDelegate_WithAnyNumberOfExecutionStages(int stages)
    {
        using var caller = new CancellationTokenSource();
        using var producer = new CancellationTokenSource();
        using var execution = new CancellationTokenSource();
        var terminal = new Terminal(execution.Token);
        var completion = new CompletionRecorder();
        ITaskScheduler scheduler = terminal.UseMiddleware(completion).UseMiddleware(new ProducerToken(producer.Token));
        var observers = Enumerable.Range(0, stages).Select(_ => new ExecutionRecorder()).ToArray();
        foreach (var observer in observers)
            scheduler = scheduler.UseMiddleware(observer);

        var token = await scheduler.Enqueue((_, current) => new ValueTask<CancellationToken>(current), null, caller.Token);

        Assert.That(token, Is.EqualTo(execution.Token));
        Assert.That(terminal.SubmittedToken, Is.EqualTo(producer.Token));
        foreach (var observer in observers)
        {
            Assert.That(observer.Contexts.Single().CancellationToken, Is.EqualTo(execution.Token));
            Assert.That(observer.Contexts.Single().ProducerCancellationToken, Is.EqualTo(producer.Token));
            Assert.That(observer.Contexts.Single().CallerCancellationToken, Is.EqualTo(caller.Token));
        }
        Assert.That(completion.Contexts.Single().CancellationToken, Is.EqualTo(execution.Token));
    }

    [Test]
    public async Task NestedCopies_PreserveMetadataAndSlots_AndApplyOnlyToTheirDownstreamInvocation()
    {
        using var execution = new CancellationTokenSource();
        using var outer = CancellationTokenSource.CreateLinkedTokenSource(execution.Token);
        using var inner = CancellationTokenSource.CreateLinkedTokenSource(outer.Token);
        var state = new object();
        var firstAnnotation = new Annotation();
        var secondAnnotation = new Annotation();
        var first = new ExecutionRecorder(outer.Token);
        var passThrough = new ExecutionRecorder();
        var second = new ExecutionRecorder(inner.Token);
        var final = new ExecutionRecorder();
        var terminal = new Terminal(execution.Token);
        var scheduler = terminal.WithAnnotation(firstAnnotation).UseMiddleware(first).UseMiddleware(passThrough)
            .WithAnnotation(secondAnnotation).UseMiddleware(second).UseMiddleware(final);

        var result = await scheduler.EnqueueWithContext(context =>
        {
            Assert.That(context.CancellationToken, Is.EqualTo(inner.Token));
            Assert.That(context.GetAnnotation<Annotation>(), Is.SameAs(secondAnnotation));
            return new ValueTask<int>(42);
        }, state, CancellationToken.None);

        Assert.That(result, Is.EqualTo(42));
        Assert.That(first.Contexts.Single().CancellationToken, Is.EqualTo(execution.Token));
        Assert.That(passThrough.Contexts.Single().CancellationToken, Is.EqualTo(outer.Token));
        Assert.That(second.Contexts.Single().CancellationToken, Is.EqualTo(outer.Token));
        Assert.That(final.Contexts.Single().CancellationToken, Is.EqualTo(inner.Token));
        Assert.That(first.Contexts.Single().GetAnnotation<Annotation>(), Is.SameAs(firstAnnotation));
        Assert.That(first.Contexts.Single().GetLocalState<object>(), Is.Not.SameAs(second.Contexts.Single().GetLocalState<object>()));
        foreach (var observer in new[] { first, passThrough, second, final })
        {
            var original = observer.Contexts.Single();
            var copy = original.WithCancellationToken(CancellationToken.None);
            Assert.That(copy, Is.Not.SameAs(original));
            Assert.That(copy.Operation, Is.SameAs(original.Operation));
            Assert.That(copy.State, Is.SameAs(state));
            Assert.That(copy.Scheduler, Is.SameAs(original.Scheduler));
            Assert.That(copy.GetAnnotation<Annotation>(), Is.SameAs(original.GetAnnotation<Annotation>()));
            Assert.That(copy.GetOrCreateLocalState(() => new object()), Is.SameAs(original.GetLocalState<object>()));
            Assert.That(copy.CallerCancellationToken, Is.EqualTo(original.CallerCancellationToken));
            Assert.That(copy.ProducerCancellationToken, Is.EqualTo(original.ProducerCancellationToken));
        }
        Assert.That(terminal.Submissions, Is.EqualTo(1));
    }

    [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
    public async Task SequentialAttempts_UseFreshTokens_AndCompleteOnceWithRootToken(ITaskFlow flow)
    {
        await using (flow)
            await VerifyAttempts(flow);
    }

    [Test]
    public async Task Adapter_AlsoPreservesSequentialAttemptTokens()
        => await VerifyAttempts(new TaskFlowSchedulerAdapter(TaskScheduler.Default));

    private static async Task VerifyAttempts(ITaskScheduler terminal)
    {
        var attempts = new TwoAttempts();
        var recorder = new ExecutionRecorder();
        var completion = new CompletionRecorder();
        var scheduler = terminal.UseMiddleware(attempts).UseMiddleware(recorder).UseMiddleware(completion);
        var calls = 0;
        var result = await scheduler.Enqueue<int>((_, token) =>
        {
            calls++;
            Assert.That(token, Is.EqualTo(attempts.Tokens[calls - 1]));
            token.ThrowIfCancellationRequested();
            return new ValueTask<int>(42);
        }, null, CancellationToken.None);

        Assert.That(result, Is.EqualTo(42));
        Assert.That(calls, Is.EqualTo(2));
        Assert.That(recorder.Contexts[0].CancellationToken.IsCancellationRequested, Is.True);
        Assert.That(recorder.Contexts[1].CancellationToken.IsCancellationRequested, Is.False);
        Assert.That(recorder.Contexts[1].GetLocalState<object>(), Is.SameAs(recorder.Contexts[0].GetLocalState<object>()));
        Assert.That(recorder.Contexts[1].Operation, Is.SameAs(recorder.Contexts[0].Operation));
        Assert.That(completion.Contexts.Single().CancellationToken, Is.EqualTo(attempts.Root!.CancellationToken));
        Assert.That(completion.Contexts.Single().CancellationToken, Is.Not.EqualTo(attempts.Tokens[1]));
    }

    [TestCase(TaskSchedulerCapacityMode.Pending)]
    [TestCase(TaskSchedulerCapacityMode.Outstanding)]
    public async Task KeyedAdmission_CopiesDoNotCreateReservations_OrReleaseUnderlyingWorkEarly(TaskSchedulerCapacityMode mode)
    {
        await using var flow = new TaskFlow();
        var admission = new BoundedAdmissionController(new BoundedAdmissionOptions { Capacity = 1, CapacityMode = mode });
        var attempts = new TwoAttempts();
        var scheduler = flow.WithKeyedScheduling<string>().WithConcurrencyLimit(1).WithBoundedAdmission(admission)
            .UseMiddleware(attempts);
        var started = Gate();
        var release = Gate();
        var calls = 0;
        var first = scheduler.Enqueue("key", (Func<CancellationToken, Task<int>>)(async token =>
        {
            calls++;
            token.ThrowIfCancellationRequested();
            started.SetResult(true);
            await release.Task;
            return 42;
        }));
        try
        {
            await started.Task.WaitAsync(Guard);
            var queued = admission.Submit(scheduler, "key", () => 7, TaskSchedulerAdmissionMode.Reject);
            var expected = mode == TaskSchedulerCapacityMode.Pending ? TaskSchedulerAdmissionResult.Accepted : TaskSchedulerAdmissionResult.Full;
            Assert.That(await queued.Admission.WaitAsync(Guard), Is.EqualTo(expected));
            if (expected == TaskSchedulerAdmissionResult.Accepted)
                Assert.That(queued.Completion.IsCompleted, Is.False);
            var shutdown = flow.DisposeAsync().AsTask();
            Assert.That(shutdown.IsCompleted, Is.False);
            release.SetResult(true);
            Assert.That(await first.WaitAsync(Guard), Is.EqualTo(42));
            Assert.That(calls, Is.EqualTo(2));
            if (expected == TaskSchedulerAdmissionResult.Full)
                await Assert.ThatAsync(async () => await queued.Completion, Throws.TypeOf<AdmissionRejectedException>());
            else
                await Assert.ThatAsync(async () => await queued.Completion, Throws.InstanceOf<OperationCanceledException>());
            await shutdown.WaitAsync(Guard);
        }
        finally { release.TrySetResult(true); }
    }

    [TestCase("success")]
    [TestCase("failure")]
    [TestCase("cancellation")]
    public async Task Completion_UsesTerminalToken_InsteadOfAttemptToken(string outcome)
    {
        using var execution = new CancellationTokenSource();
        using var attempt = new CancellationTokenSource();
        await attempt.CancelAsync();
        var observer = new CompletionRecorder();
        var terminal = new Terminal(execution.Token);
        var failure = new InvalidOperationException("operation");
        var scheduler = terminal.UseMiddleware(new ExecutionRecorder(attempt.Token)).UseMiddleware(observer);
        var operation = scheduler.Enqueue<int>((_, token) =>
        {
            Assert.That(token, Is.EqualTo(attempt.Token));
            if (outcome == "cancellation")
                token.ThrowIfCancellationRequested();
            if (outcome == "failure")
                throw failure;
            return new ValueTask<int>(42);
        }, null, CancellationToken.None);
        if (outcome == "success")
            Assert.That(await operation, Is.EqualTo(42));
        else if (outcome == "failure")
            Assert.That(Assert.ThrowsAsync<InvalidOperationException>(async () => await operation), Is.SameAs(failure));
        else
            await Assert.ThatAsync(async () => await operation, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(observer.Contexts.Single().CancellationToken, Is.EqualTo(execution.Token));
        Assert.That(observer.Contexts.Single().CancellationToken.IsCancellationRequested, Is.False);
    }

    [Test]
    public void CompletionReplacements_SurvivePassThroughAndCallbackFailureFallback()
    {
        using var execution = new CancellationTokenSource();
        using var replacement = new CancellationTokenSource();
        using var nestedReplacement = new CancellationTokenSource();
        var transform = new CompletionRecorder(replacement.Token);
        var passThrough = new CompletionRecorder();
        var nestedTransform = new CompletionRecorder(nestedReplacement.Token);
        var failure = new InvalidOperationException("completion");
        var throwing = new CompletionRecorder(failure: failure);
        var last = new CompletionRecorder();
        var scheduler = new Terminal(execution.Token).UseMiddleware(transform).UseMiddleware(passThrough)
            .UseMiddleware(nestedTransform).UseMiddleware(throwing).UseMiddleware(last);

        var thrown = Assert.ThrowsAsync<InvalidOperationException>(async () => await scheduler.Enqueue(() => 42));

        Assert.That(thrown, Is.SameAs(failure));
        Assert.That(transform.Contexts.Single().CancellationToken, Is.EqualTo(execution.Token));
        foreach (var observer in new[] { passThrough, nestedTransform })
            Assert.That(observer.Contexts.Single().CancellationToken, Is.EqualTo(replacement.Token));
        foreach (var observer in new[] { throwing, last })
            Assert.That(observer.Contexts.Single().CancellationToken, Is.EqualTo(nestedReplacement.Token));
        Assert.That(last.Exceptions.Single(), Is.SameAs(failure));
    }

    [Test]
    public async Task CompletionThrowAfterReplacedContinuation_DoesNotRepeatDownstreamOrQuiescence()
    {
        using var execution = new CancellationTokenSource();
        using var replacement = new CancellationTokenSource();
        var failure = new InvalidOperationException("after continuation");
        var wrapping = new WrappingCompletion(replacement.Token, throwAfter: failure);
        var paused = new PausedCompletion();
        var last = new CompletionRecorder();
        var terminal = new Terminal(execution.Token);
        var scheduler = terminal.UseMiddleware(wrapping).UseMiddleware(paused).UseMiddleware(last);
        var quiesced = 0;
        var operation = scheduler.Enqueue(() => 42);
        try
        {
            var downstream = await paused.Entered.Task.WaitAsync(Guard);
            downstream.Operation.OnQuiesced(() => Interlocked.Increment(ref quiesced));
            Assert.That(downstream.CancellationToken, Is.EqualTo(replacement.Token));
            Assert.That(operation.IsCompleted, Is.False);
            Assert.That(last.Contexts, Is.Empty);
            paused.Release.SetResult(true);
            var thrown = Assert.ThrowsAsync<InvalidOperationException>(async () => await operation.WaitAsync(Guard));
            Assert.That(thrown, Is.SameAs(failure));
            Assert.That(wrapping.Context!.CancellationToken, Is.EqualTo(execution.Token));
            Assert.That(wrapping.ObservedException, Is.Null);
            Assert.That(paused.Calls, Is.EqualTo(1));
            Assert.That(last.Contexts.Single().CancellationToken, Is.EqualTo(replacement.Token));
            Assert.That(last.Exceptions.Single(), Is.Null, "The later upstream failure must not notify downstream again.");
            Assert.That(terminal.Submissions, Is.EqualTo(1));
            Assert.That(quiesced, Is.EqualTo(1));
        }
        finally { paused.Release.TrySetResult(true); }
    }

    [Test]
    public async Task DownstreamCompletionFailure_ReturnsAnOutcomeThatUpstreamCanRecover()
    {
        using var execution = new CancellationTokenSource();
        using var replacement = new CancellationTokenSource();
        var failure = new InvalidOperationException("downstream callback");
        var wrapping = new WrappingCompletion(replacement.Token, recover: true);
        var paused = new PausedCompletion(failure: failure);
        var last = new CompletionRecorder();
        var scheduler = new Terminal(execution.Token).UseMiddleware(wrapping).UseMiddleware(paused).UseMiddleware(last);
        var operation = scheduler.Enqueue(() => 42);
        try
        {
            await paused.Entered.Task.WaitAsync(Guard);
            paused.Release.SetResult(true);
            Assert.That(await operation.WaitAsync(Guard), Is.EqualTo(84));
            Assert.That(wrapping.CaughtContinuationException, Is.False, "Completion failures are represented as outcomes.");
            Assert.That(wrapping.ObservedException, Is.SameAs(failure));
            Assert.That(wrapping.Context!.CancellationToken, Is.EqualTo(execution.Token));
            Assert.That(last.Contexts.Single().CancellationToken, Is.EqualTo(replacement.Token));
            Assert.That(last.Exceptions.Single(), Is.SameAs(failure));
            Assert.That(paused.Calls, Is.EqualTo(1));
        }
        finally { paused.Release.TrySetResult(true); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CompletionProducerSnapshot_SurvivesLateSubmission_InRecursionAndExceptionFallback(bool throwBeforeContinuation)
    {
        var clock = new FakeTimeProvider();
        using var caller = new CancellationTokenSource();
        using var producer = new CancellationTokenSource();
        using var execution = new CancellationTokenSource();
        using var replacement = new CancellationTokenSource();
        var failure = new InvalidOperationException("paused completion");
        var paused = new PausedCompletion(replacement.Token, throwBeforeContinuation ? failure : null);
        var last = new CompletionRecorder();
        var gate = new EnqueueGate();
        var terminal = new Terminal(execution.Token);
        var scheduler = terminal.UseMiddleware(paused).UseMiddleware(last).UseMiddleware(new ProducerToken(producer.Token))
            .UseMiddleware(gate).WithTimeout(TimeSpan.FromMinutes(1), clock);
        var operation = scheduler.Enqueue<int>((_, _) => new ValueTask<int>(42), null, caller.Token);
        try
        {
            await gate.Entered.Task.WaitAsync(Guard);
            clock.Advance(TimeSpan.FromMinutes(1));
            var upstream = await paused.Entered.Task.WaitAsync(Guard);
            var copy = upstream.WithCancellationToken(replacement.Token);
            Assert.That(upstream.CancellationToken, Is.EqualTo(caller.Token));
            Assert.That(upstream.ProducerCancellationToken, Is.EqualTo(caller.Token));
            Assert.That(paused.IncomingException, Is.TypeOf<TimeoutException>());
            Assert.That(terminal.Submissions, Is.Zero);
            gate.Release.SetResult(true);
            await gate.Completed.Task.WaitAsync(Guard);
            Assert.That(upstream.Operation.ProducerCancellationToken, Is.EqualTo(producer.Token));
            Assert.That(upstream.ProducerCancellationToken, Is.EqualTo(caller.Token));
            Assert.That(copy.ProducerCancellationToken, Is.EqualTo(caller.Token));
            Assert.That(upstream.WithCancellationToken(execution.Token).ProducerCancellationToken, Is.EqualTo(caller.Token));
            paused.Release.SetResult(true);
            if (throwBeforeContinuation)
                Assert.That(Assert.ThrowsAsync<InvalidOperationException>(async () => await operation.WaitAsync(Guard)), Is.SameAs(failure));
            else
                Assert.That(Assert.ThrowsAsync<TimeoutException>(async () => await operation.WaitAsync(Guard)), Is.SameAs(paused.IncomingException));
            var downstream = last.Contexts.Single();
            Assert.That(downstream.CancellationToken, Is.EqualTo(throwBeforeContinuation ? caller.Token : replacement.Token));
            Assert.That(downstream.ProducerCancellationToken, Is.EqualTo(caller.Token));
            Assert.That(downstream.CallerCancellationToken, Is.EqualTo(caller.Token));
            Assert.That(last.Exceptions.Single(), Is.SameAs(throwBeforeContinuation ? failure : paused.IncomingException));
            Assert.That(paused.Calls, Is.EqualTo(1));
            Assert.That(terminal.Submissions, Is.EqualTo(1));
        }
        finally
        {
            gate.Release.TrySetResult(true);
            paused.Release.TrySetResult(true);
        }
    }

    [Test]
    public async Task PreterminalTimeout_SnapshotsFallbackProducerToken_EvenIfSubmissionOccursLater()
    {
        var clock = new FakeTimeProvider();
        using var caller = new CancellationTokenSource();
        using var producer = new CancellationTokenSource();
        using var execution = new CancellationTokenSource();
        var gate = new EnqueueGate();
        var observer = new CompletionRecorder();
        var terminal = new Terminal(execution.Token);
        var scheduler = terminal.UseMiddleware(observer).UseMiddleware(new ProducerToken(producer.Token))
            .UseMiddleware(gate).WithTimeout(TimeSpan.FromMinutes(1), clock);
        var operation = scheduler.Enqueue<int>((_, _) => new ValueTask<int>(42), null, caller.Token);
        try
        {
            await gate.Entered.Task.WaitAsync(Guard);
            clock.Advance(TimeSpan.FromMinutes(1));
            await Assert.ThatAsync(async () => await operation.WaitAsync(Guard), Throws.TypeOf<TimeoutException>());
            var context = observer.Contexts.Single();
            var copy = context.WithCancellationToken(CancellationToken.None);
            Assert.That(context.CancellationToken, Is.EqualTo(caller.Token));
            Assert.That(context.ProducerCancellationToken, Is.EqualTo(caller.Token));
            Assert.That(terminal.Submissions, Is.Zero);
            gate.Release.SetResult(true);
            await gate.Completed.Task.WaitAsync(Guard);
            Assert.That(terminal.SubmittedToken, Is.EqualTo(producer.Token));
            Assert.That(context.Operation.ProducerCancellationToken, Is.EqualTo(producer.Token));
            Assert.That(context.ProducerCancellationToken, Is.EqualTo(caller.Token));
            Assert.That(copy.ProducerCancellationToken, Is.EqualTo(caller.Token));
            Assert.That(context.WithCancellationToken(execution.Token).ProducerCancellationToken, Is.EqualTo(caller.Token));
            Assert.That(observer.Contexts, Has.Count.EqualTo(1));
        }
        finally { gate.Release.TrySetResult(true); }
    }

    [Test]
    public async Task PreterminalSubmissionFailure_UsesProducerTokenWithoutRelinkingCanceledCaller()
    {
        using var caller = new CancellationTokenSource();
        using var producer = new CancellationTokenSource();
        await caller.CancelAsync();
        var observer = new CompletionRecorder();
        var failure = new InvalidOperationException("terminal rejected");
        var scheduler = new Terminal(CancellationToken.None, failure).UseMiddleware(observer).UseMiddleware(new ProducerToken(producer.Token));
        var thrown = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await scheduler.Enqueue<int>((_, _) => new ValueTask<int>(42), null, caller.Token));
        Assert.That(thrown, Is.SameAs(failure));
        var context = observer.Contexts.Single();
        Assert.That(context.CallerCancellationToken, Is.EqualTo(caller.Token));
        Assert.That(context.CancellationToken, Is.EqualTo(producer.Token));
        Assert.That(context.ProducerCancellationToken, Is.EqualTo(producer.Token));
        Assert.That(context.CancellationToken.IsCancellationRequested, Is.False);
    }

    [Test]
    public async Task Shutdown_CancelsMiddlewareAwait_BeforeTheUserDelegateRuns()
    {
        foreach (var flow in TaskFlows.CreateTaskFlows())
        {
            var holding = new HoldingExecution();
            var invoked = false;
            var operation = flow.UseMiddleware(holding).Enqueue(() => invoked = true);
            try
            {
                await holding.Entered.Task.WaitAsync(Guard);
                await flow.DisposeAsync().AsTask().WaitAsync(Guard);
                await Assert.ThatAsync(async () => await operation, Throws.InstanceOf<OperationCanceledException>());
                Assert.That(invoked, Is.False);
            }
            finally
            {
                holding.Release.TrySetResult(true);
                await flow.DisposeAsync();
            }
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AnotherOperationsContext_IsRejectedByExecutionAndCompletionContinuations(bool invalidDuringCompletion)
    {
        var foreign = new ExecutionRecorder();
        await new Terminal(CancellationToken.None).UseMiddleware(foreign).Enqueue(() => 1);
        using var caller = new CancellationTokenSource();
        using var producer = new CancellationTokenSource();
        using var execution = new CancellationTokenSource();
        var invalid = new ForeignContext(foreign.Contexts.Single(), invalidDuringCompletion);
        var actualCalls = 0;
        var completion = new CompletionRecorder();
        var scheduler = new Terminal(execution.Token).UseMiddleware(invalid).UseMiddleware(completion)
            .UseMiddleware(new ProducerToken(producer.Token));
        var failure = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await scheduler.Enqueue<int>((_, _) => new ValueTask<int>(++actualCalls), null, caller.Token));
        Assert.That(failure!.Message, Does.Contain("same operation"));
        Assert.That(actualCalls, Is.EqualTo(invalidDuringCompletion ? 1 : 0));
        var context = completion.Contexts.Single();
        Assert.That(context.CancellationToken, Is.EqualTo(execution.Token));
        Assert.That(context.ProducerCancellationToken, Is.EqualTo(producer.Token));
        Assert.That(context.CallerCancellationToken, Is.EqualTo(caller.Token));
        Assert.That(completion.Exceptions.Single(), Is.SameAs(failure));
        Assert.That(context.Operation, Is.Not.SameAs(foreign.Contexts.Single().Operation));
    }

    private sealed class Annotation : IOperationAnnotation;

    private sealed class ExecutionRecorder(CancellationToken? replacement = null) : ITaskSchedulerExecutionMiddleware
    {
        public List<TaskSchedulerOperationContext> Contexts { get; } = new();
        public ValueTask<TResult> InvokeAsync<TResult>(TaskSchedulerOperationContext context, TaskSchedulerExecutionDelegate<TResult> continuation)
        {
            Contexts.Add(context);
            context.GetOrCreateLocalState(() => new object());
            return continuation(replacement.HasValue ? context.WithCancellationToken(replacement.Value) : context);
        }
    }

    private sealed class CompletionRecorder(CancellationToken? replacement = null, Exception? failure = null) : ITaskSchedulerCompletionMiddleware
    {
        public List<TaskSchedulerOperationContext> Contexts { get; } = new();
        public List<Exception?> Exceptions { get; } = new();
        public ValueTask<TaskSchedulerOperationOutcome<TResult>> InvokeAsync<TResult>(TaskSchedulerOperationContext context,
            TaskSchedulerOperationOutcome<TResult> outcome, TaskSchedulerCompletionDelegate<TResult> continuation)
        {
            Contexts.Add(context);
            Exceptions.Add(outcome.Exception);
            if (failure != null)
                throw failure;
            return continuation(replacement.HasValue ? context.WithCancellationToken(replacement.Value) : context, outcome);
        }
    }

    private sealed class PausedCompletion(CancellationToken? replacement = null, Exception? failure = null) : ITaskSchedulerCompletionMiddleware
    {
        public TaskCompletionSource<TaskSchedulerOperationContext> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = Gate();
        public int Calls { get; private set; }
        public Exception? IncomingException { get; private set; }

        public async ValueTask<TaskSchedulerOperationOutcome<TResult>> InvokeAsync<TResult>(TaskSchedulerOperationContext context,
            TaskSchedulerOperationOutcome<TResult> outcome, TaskSchedulerCompletionDelegate<TResult> continuation)
        {
            Calls++;
            IncomingException = outcome.Exception;
            Entered.TrySetResult(context);
            await Release.Task;
            if (failure != null)
                throw failure;
            return await continuation(replacement.HasValue ? context.WithCancellationToken(replacement.Value) : context, outcome);
        }
    }

    private sealed class WrappingCompletion(CancellationToken replacement, Exception? throwAfter = null, bool recover = false) : ITaskSchedulerCompletionMiddleware
    {
        public TaskSchedulerOperationContext? Context { get; private set; }
        public Exception? ObservedException { get; private set; }
        public bool CaughtContinuationException { get; private set; }

        public async ValueTask<TaskSchedulerOperationOutcome<TResult>> InvokeAsync<TResult>(TaskSchedulerOperationContext context,
            TaskSchedulerOperationOutcome<TResult> outcome, TaskSchedulerCompletionDelegate<TResult> continuation)
        {
            Context = context;
            TaskSchedulerOperationOutcome<TResult> downstream;
            try
            {
                downstream = await continuation(context.WithCancellationToken(replacement), outcome);
            }
            catch (InvalidOperationException)
            {
                CaughtContinuationException = true;
                throw;
            }
            ObservedException = downstream.Exception;
            if (throwAfter != null)
                throw throwAfter;
            return recover ? TaskSchedulerOperationOutcome<TResult>.FromResult((TResult)(object)84) : downstream;
        }
    }

    private sealed class TwoAttempts : ITaskSchedulerExecutionMiddleware
    {
        public List<CancellationToken> Tokens { get; } = new();
        public TaskSchedulerOperationContext? Root { get; private set; }
        public async ValueTask<TResult> InvokeAsync<TResult>(TaskSchedulerOperationContext context, TaskSchedulerExecutionDelegate<TResult> continuation)
        {
            Root = context;
            using (var first = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken))
            {
                Tokens.Add(first.Token);
                await first.CancelAsync();
                await Assert.ThatAsync(async () => await continuation(context.WithCancellationToken(first.Token)),
                    Throws.InstanceOf<OperationCanceledException>());
            }
            using var second = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            Tokens.Add(second.Token);
            Assert.That(context.CancellationToken.IsCancellationRequested, Is.False);
            return await continuation(context.WithCancellationToken(second.Token));
        }
    }

    private sealed class Terminal(CancellationToken executionToken, Exception? failure = null) : ITaskScheduler
    {
        public int Submissions { get; private set; }
        public CancellationToken SubmittedToken { get; private set; }
        public async Task<TResult> Enqueue<TResult>(Func<object?, CancellationToken, ValueTask<TResult>> taskFunc, object? state, CancellationToken cancellationToken)
        {
            Submissions++;
            SubmittedToken = cancellationToken;
            if (failure != null)
                throw failure;
            return await taskFunc(state, executionToken);
        }
    }

    private sealed class ProducerToken(CancellationToken token) : ITaskSchedulerEnqueueMiddleware
    {
        public Task<TResult> InvokeAsync<TResult>(TaskSchedulerEnqueueContext<TResult> context, TaskSchedulerEnqueueDelegate<TResult> continuation)
            => continuation(context.WithCancellationToken(token));
    }

    private sealed class EnqueueGate : ITaskSchedulerEnqueueMiddleware
    {
        public TaskCompletionSource<bool> Entered { get; } = Gate();
        public TaskCompletionSource<bool> Release { get; } = Gate();
        public TaskCompletionSource<bool> Completed { get; } = Gate();
        public async Task<TResult> InvokeAsync<TResult>(TaskSchedulerEnqueueContext<TResult> context, TaskSchedulerEnqueueDelegate<TResult> continuation)
        {
            Entered.SetResult(true);
            await Release.Task;
            try
            { return await continuation(context); }
            finally { Completed.SetResult(true); }
        }
    }

    private sealed class HoldingExecution : ITaskSchedulerExecutionMiddleware
    {
        public TaskCompletionSource<bool> Entered { get; } = Gate();
        public TaskCompletionSource<bool> Release { get; } = Gate();
        public async ValueTask<TResult> InvokeAsync<TResult>(TaskSchedulerOperationContext context, TaskSchedulerExecutionDelegate<TResult> continuation)
        {
            Entered.SetResult(true);
            await Release.Task.WaitAsync(context.CancellationToken);
            return await continuation(context);
        }
    }

    private sealed class ForeignContext(TaskSchedulerOperationContext foreign, bool invalidDuringCompletion) : ITaskSchedulerExecutionMiddleware, ITaskSchedulerCompletionMiddleware
    {
        public ValueTask<TResult> InvokeAsync<TResult>(TaskSchedulerOperationContext context, TaskSchedulerExecutionDelegate<TResult> continuation)
            => continuation(invalidDuringCompletion ? context : foreign.WithCancellationToken(CancellationToken.None));
        public ValueTask<TaskSchedulerOperationOutcome<TResult>> InvokeAsync<TResult>(TaskSchedulerOperationContext context,
            TaskSchedulerOperationOutcome<TResult> outcome, TaskSchedulerCompletionDelegate<TResult> continuation)
            => continuation(invalidDuringCompletion ? foreign.WithCancellationToken(CancellationToken.None) : context, outcome);
    }
}