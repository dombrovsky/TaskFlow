namespace TaskFlow.Tests.Extensions
{
    using NUnit.Framework;
    using System.Collections.Concurrent;
    using System.Threading.Tasks.Flow;

    [TestFixture]
    internal sealed class TaskSchedulerMiddlewareExtensionsFixture
    {
        private static readonly string[] ExpectedPhaseEvents =
        {
            "enqueue:second", "enqueue:first", "terminal", "execute:first", "execute:second",
            "operation", "complete:first", "complete:second", "terminal-return"
        };

        [Test]
        public async Task Pipeline_UsesPhaseOrderingAndOneTerminalDelegate()
        {
            var events = new List<string>();
            var terminal = new RecordingTerminal(events);
            var scheduler = terminal
                .UseMiddleware(new RecordingMiddleware("first", events))
                .UseMiddleware(new RecordingMiddleware("second", events));

            Assert.That(await scheduler.Enqueue(() => { events.Add("operation"); return 42; }), Is.EqualTo(42));
            Assert.That(terminal.EnqueueCount, Is.EqualTo(1));
            Assert.That(events, Is.EqualTo(ExpectedPhaseEvents));
        }

        [Test]
        public async Task Metadata_IsForwardScopedShadowedAndBranchIndependent()
        {
            var observations = new ConcurrentBag<string>();
            var root = new InlineTerminal().WithAnnotation(new TestAnnotation("root"));
            var parent = root.UseMiddleware(new AnnotationMiddleware("parent", observations));
            var left = parent.WithAnnotation(new TestAnnotation("left"))
                .UseMiddleware(new AnnotationMiddleware("left", observations));
            var right = parent.WithAnnotation(new TestAnnotation("right"))
                .UseMiddleware(new AnnotationMiddleware("right", observations));

            await Task.WhenAll(left.Enqueue(() => 1), right.Enqueue(() => 2));

            Assert.That(observations.Count(x => x == "parent:root"), Is.EqualTo(2));
            Assert.That(observations, Does.Contain("left:left"));
            Assert.That(observations, Does.Contain("right:right"));
        }

        [Test]
        public async Task EnqueueMiddleware_MayShortCircuitWithoutReachingTerminal()
        {
            var terminal = new RecordingTerminal(new List<string>());
            var scheduler = terminal.UseMiddleware(new ConstantMiddleware(17));

            Assert.That(await scheduler.Enqueue(() => 42), Is.EqualTo(17));
            Assert.That(terminal.EnqueueCount, Is.Zero);
        }

        [Test]
        public async Task ExecutionMiddleware_MayInvokeContinuationMoreThanOnceInOneQueueTurn()
        {
            var terminal = new RecordingTerminal(new List<string>());
            var calls = 0;
            var scheduler = terminal.UseMiddleware(new TwiceMiddleware());

            Assert.That(await scheduler.Enqueue(() => ++calls), Is.EqualTo(2));
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(terminal.EnqueueCount, Is.EqualTo(1));
        }

        [Test]
        public async Task CompoundMiddleware_SharesOnlyItsRegistrationLocalState()
        {
            var middleware = new CompoundStateMiddleware();
            var scheduler = new InlineTerminal().UseMiddleware(middleware);

            await Task.WhenAll(scheduler.Enqueue(() => 1), scheduler.Enqueue(() => 2));

            Assert.That(middleware.ObservedIds, Has.Count.EqualTo(2));
            Assert.That(middleware.ObservedIds.Distinct().Count(), Is.EqualTo(2));
        }

        [Test]
        public async Task Terminal_ReceivesOriginalStateAndProducerToken()
        {
            var terminal = new RecordingTerminal(new List<string>());
            var state = new object();
            using var source = new CancellationTokenSource();
            var scheduler = terminal.UseMiddleware(new AnnotationMiddleware("unused", new ConcurrentBag<string>()));

            await scheduler.Enqueue((s, _) => new ValueTask<object?>(s), state, source.Token);

            Assert.That(terminal.State, Is.SameAs(state));
            Assert.That(terminal.CancellationToken, Is.EqualTo(source.Token));
        }

        [Test]
        public async Task EnqueueWithContext_ReceivesFinalStateTokensAndAnnotations()
        {
            var state = new object();
            using var source = new CancellationTokenSource();
            var scheduler = new InlineTerminal().WithAnnotation(new TestAnnotation("final"));

            var result = await scheduler.EnqueueWithContext(
                context => new ValueTask<(object?, CancellationToken, CancellationToken, string?)>((
                    context.State,
                    context.CallerCancellationToken,
                    context.CancellationToken,
                    context.GetAnnotation<TestAnnotation>()?.Value)),
                state,
                source.Token);

            Assert.That(result.Item1, Is.SameAs(state));
            Assert.That(result.Item2, Is.EqualTo(source.Token));
            Assert.That(result.Item3, Is.EqualTo(source.Token));
            Assert.That(result.Item4, Is.EqualTo("final"));
        }

        [Test]
        public async Task EnqueueMiddleware_MayReplaceProducerTokenWithoutReplacingCallerToken()
        {
            using var callerSource = new CancellationTokenSource();
            using var producerSource = new CancellationTokenSource();
            var terminal = new RecordingTerminal(new List<string>());
            var observer = new TokenObservingMiddleware();
            var scheduler = terminal
                .UseMiddleware(observer)
                .UseMiddleware(new ProducerTokenMiddleware(producerSource.Token));

            await scheduler.Enqueue(
                (_, _) => new ValueTask<int>(42),
                null,
                callerSource.Token);

            Assert.That(terminal.CancellationToken, Is.EqualTo(producerSource.Token));
            Assert.That(observer.CallerToken, Is.EqualTo(callerSource.Token));
            Assert.That(observer.ProducerToken, Is.EqualTo(producerSource.Token));
        }

        [Test]
        public void TerminalEnqueueFailure_IsProcessedByCompletionExactlyOnce()
        {
            var failure = new InvalidOperationException("rejected");
            var completion = new ObservingCompletionMiddleware();
            var scheduler = new ThrowingTerminal(failure).UseMiddleware(completion);

            var thrown = Assert.ThrowsAsync<InvalidOperationException>(async () => await scheduler.Enqueue(() => 42));

            Assert.That(thrown, Is.SameAs(failure));
            Assert.That(completion.CallCount, Is.EqualTo(1));
            Assert.That(completion.ObservedException, Is.SameAs(failure));
        }

        [Test]
        public void ExecutionFailure_PreservesExceptionAndRunsCompletionExactlyOnce()
        {
            var failure = CreateFailureWithCapturedStack();
            var completion = new ObservingCompletionMiddleware();
            var scheduler = new InlineTerminal().UseMiddleware(completion);

            var thrown = Assert.ThrowsAsync<InvalidOperationException>(async () => await scheduler.Enqueue<int>(
                (_, _) => ValueTask.FromException<int>(failure),
                null,
                CancellationToken.None));

            Assert.That(thrown, Is.SameAs(failure));
            Assert.That(thrown!.StackTrace, Does.Contain(nameof(CreateFailureWithCapturedStack)));
            Assert.That(completion.CallCount, Is.EqualTo(1));
            Assert.That(completion.ObservedException, Is.SameAs(failure));
        }

        [Test]
        public void CompletionReplacement_IsVisibleToLaterMiddlewareAndCaller()
        {
            var original = new InvalidOperationException("original");
            var replacement = new NotSupportedException("replacement");
            var observer = new ObservingCompletionMiddleware();
            var scheduler = new InlineTerminal()
                .UseMiddleware(new ReplacingCompletionMiddleware(replacement))
                .UseMiddleware(observer);

            var thrown = Assert.ThrowsAsync<NotSupportedException>(async () => await scheduler.Enqueue<int>(
                (_, _) => ValueTask.FromException<int>(original),
                null,
                CancellationToken.None));

            Assert.That(thrown, Is.SameAs(replacement));
            Assert.That(observer.ObservedException, Is.SameAs(replacement));
        }

        [Test]
        public void CompletionFailureBeforeContinuation_IsVisibleToLaterMiddleware()
        {
            var replacement = new NotSupportedException("callback failed");
            var observer = new ObservingCompletionMiddleware();
            var scheduler = new InlineTerminal()
                .UseMiddleware(new ThrowingCompletionMiddleware(replacement, throwAfterContinuation: false))
                .UseMiddleware(observer);

            var thrown = Assert.ThrowsAsync<NotSupportedException>(async () => await scheduler.Enqueue(() => 42));

            Assert.That(thrown, Is.SameAs(replacement));
            Assert.That(observer.CallCount, Is.EqualTo(1));
            Assert.That(observer.ObservedException, Is.SameAs(replacement));
        }

        [Test]
        public void CompletionFailureAfterContinuation_ReplacesFinalOutcomeWithoutRepeatingLaterMiddleware()
        {
            var replacement = new NotSupportedException("callback failed after next");
            var observer = new ObservingCompletionMiddleware();
            var scheduler = new InlineTerminal()
                .UseMiddleware(new ThrowingCompletionMiddleware(replacement, throwAfterContinuation: true))
                .UseMiddleware(observer);

            var thrown = Assert.ThrowsAsync<NotSupportedException>(async () => await scheduler.Enqueue(() => 42));

            Assert.That(thrown, Is.SameAs(replacement));
            Assert.That(observer.CallCount, Is.EqualTo(1));
            Assert.That(observer.ObservedException, Is.Null);
        }

        [Test]
        public void UseMiddleware_RejectsMarkerWithoutPhase()
        {
            Assert.That(
                () => new InlineTerminal().UseMiddleware(new MarkerOnlyMiddleware()),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("middleware"));
        }

        private static InvalidOperationException CreateFailureWithCapturedStack()
        {
            try
            {
                throw new InvalidOperationException("failure");
            }
            catch (InvalidOperationException exception)
            {
                return exception;
            }
        }

        private sealed class TestAnnotation : IOperationAnnotation
        {
            public TestAnnotation(string value) => Value = value;
            public string Value { get; }
        }

        private sealed class RecordingMiddleware : ITaskSchedulerEnqueueMiddleware, ITaskSchedulerExecutionMiddleware, ITaskSchedulerCompletionMiddleware
        {
            private readonly string _name;
            private readonly IList<string> _events;
            public RecordingMiddleware(string name, IList<string> events) { _name = name; _events = events; }

            public async Task<TResult> InvokeAsync<TResult>(TaskSchedulerEnqueueContext<TResult> context, TaskSchedulerEnqueueDelegate<TResult> continuation)
            {
                _events.Add("enqueue:" + _name);
                return await continuation(context);
            }

            public async ValueTask<TResult> InvokeAsync<TResult>(TaskSchedulerOperationContext context, TaskSchedulerExecutionDelegate<TResult> continuation)
            {
                _events.Add("execute:" + _name);
                return await continuation(context);
            }

            public async ValueTask<TaskSchedulerOperationOutcome<TResult>> InvokeAsync<TResult>(TaskSchedulerOperationContext context, TaskSchedulerOperationOutcome<TResult> outcome, TaskSchedulerCompletionDelegate<TResult> continuation)
            {
                _events.Add("complete:" + _name);
                return await continuation(context, outcome);
            }
        }

        private sealed class AnnotationMiddleware : ITaskSchedulerExecutionMiddleware
        {
            private readonly string _name;
            private readonly ConcurrentBag<string> _observations;
            public AnnotationMiddleware(string name, ConcurrentBag<string> observations) { _name = name; _observations = observations; }
            public ValueTask<TResult> InvokeAsync<TResult>(TaskSchedulerOperationContext context, TaskSchedulerExecutionDelegate<TResult> continuation)
            {
                _observations.Add(_name + ":" + context.GetAnnotation<TestAnnotation>()?.Value);
                return continuation(context);
            }
        }

        private sealed class ConstantMiddleware : ITaskSchedulerEnqueueMiddleware
        {
            private readonly int _value;
            public ConstantMiddleware(int value) => _value = value;
            public Task<TResult> InvokeAsync<TResult>(TaskSchedulerEnqueueContext<TResult> context, TaskSchedulerEnqueueDelegate<TResult> continuation)
                => Task.FromResult((TResult)(object)_value);
        }

        private sealed class ProducerTokenMiddleware : ITaskSchedulerEnqueueMiddleware
        {
            private readonly CancellationToken _producerToken;
            public ProducerTokenMiddleware(CancellationToken producerToken) => _producerToken = producerToken;
            public Task<TResult> InvokeAsync<TResult>(TaskSchedulerEnqueueContext<TResult> context, TaskSchedulerEnqueueDelegate<TResult> continuation)
                => continuation(context.WithCancellationToken(_producerToken));
        }

        private sealed class TokenObservingMiddleware : ITaskSchedulerExecutionMiddleware
        {
            public CancellationToken CallerToken { get; private set; }
            public CancellationToken ProducerToken { get; private set; }
            public ValueTask<TResult> InvokeAsync<TResult>(TaskSchedulerOperationContext context, TaskSchedulerExecutionDelegate<TResult> continuation)
            {
                CallerToken = context.CallerCancellationToken;
                ProducerToken = context.CancellationToken;
                return continuation(context);
            }
        }

        private sealed class ObservingCompletionMiddleware : ITaskSchedulerCompletionMiddleware
        {
            public int CallCount { get; private set; }
            public Exception? ObservedException { get; private set; }
            public ValueTask<TaskSchedulerOperationOutcome<TResult>> InvokeAsync<TResult>(TaskSchedulerOperationContext context, TaskSchedulerOperationOutcome<TResult> outcome, TaskSchedulerCompletionDelegate<TResult> continuation)
            {
                CallCount++;
                ObservedException = outcome.Exception;
                return continuation(context, outcome);
            }
        }

        private sealed class ReplacingCompletionMiddleware : ITaskSchedulerCompletionMiddleware
        {
            private readonly Exception _replacement;
            public ReplacingCompletionMiddleware(Exception replacement) => _replacement = replacement;
            public ValueTask<TaskSchedulerOperationOutcome<TResult>> InvokeAsync<TResult>(TaskSchedulerOperationContext context, TaskSchedulerOperationOutcome<TResult> outcome, TaskSchedulerCompletionDelegate<TResult> continuation)
                => continuation(context, TaskSchedulerOperationOutcome<TResult>.FromException(_replacement));
        }

        private sealed class ThrowingCompletionMiddleware : ITaskSchedulerCompletionMiddleware
        {
            private readonly Exception _exception;
            private readonly bool _throwAfterContinuation;
            public ThrowingCompletionMiddleware(Exception exception, bool throwAfterContinuation)
            {
                _exception = exception;
                _throwAfterContinuation = throwAfterContinuation;
            }

            public async ValueTask<TaskSchedulerOperationOutcome<TResult>> InvokeAsync<TResult>(TaskSchedulerOperationContext context, TaskSchedulerOperationOutcome<TResult> outcome, TaskSchedulerCompletionDelegate<TResult> continuation)
            {
                if (!_throwAfterContinuation) throw _exception;
                _ = await continuation(context, outcome);
                throw _exception;
            }
        }

        private sealed class MarkerOnlyMiddleware : ITaskSchedulerMiddleware
        {
        }

        private sealed class TwiceMiddleware : ITaskSchedulerExecutionMiddleware
        {
            public async ValueTask<TResult> InvokeAsync<TResult>(TaskSchedulerOperationContext context, TaskSchedulerExecutionDelegate<TResult> continuation)
            {
                _ = await continuation(context);
                return await continuation(context);
            }
        }

        private sealed class CompoundStateMiddleware : ITaskSchedulerEnqueueMiddleware, ITaskSchedulerExecutionMiddleware
        {
            private int _nextId;
            public ConcurrentBag<int> ObservedIds { get; } = new ConcurrentBag<int>();
            public Task<TResult> InvokeAsync<TResult>(TaskSchedulerEnqueueContext<TResult> context, TaskSchedulerEnqueueDelegate<TResult> continuation)
            {
                context.GetOrCreateLocalState(() => new LocalState(Interlocked.Increment(ref _nextId)));
                return continuation(context);
            }

            public ValueTask<TResult> InvokeAsync<TResult>(TaskSchedulerOperationContext context, TaskSchedulerExecutionDelegate<TResult> continuation)
            {
                ObservedIds.Add(context.GetLocalState<LocalState>()!.Id);
                return continuation(context);
            }
        }

        private sealed class LocalState
        {
            public LocalState(int id) => Id = id;
            public int Id { get; }
        }

        private sealed class RecordingTerminal : ITaskScheduler
        {
            private readonly IList<string> _events;
            public RecordingTerminal(IList<string> events) => _events = events;
            public int EnqueueCount { get; private set; }
            public object? State { get; private set; }
            public CancellationToken CancellationToken { get; private set; }

            public async Task<TResult> Enqueue<TResult>(Func<object?, CancellationToken, ValueTask<TResult>> taskFunc, object? state, CancellationToken cancellationToken)
            {
                EnqueueCount++;
                State = state;
                CancellationToken = cancellationToken;
                _events.Add("terminal");
                var result = await taskFunc(state, cancellationToken);
                _events.Add("terminal-return");
                return result;
            }
        }

        private sealed class InlineTerminal : ITaskScheduler
        {
            public async Task<TResult> Enqueue<TResult>(Func<object?, CancellationToken, ValueTask<TResult>> taskFunc, object? state, CancellationToken cancellationToken)
                => await taskFunc(state, cancellationToken);
        }

        private sealed class ThrowingTerminal : ITaskScheduler
        {
            private readonly Exception _exception;
            public ThrowingTerminal(Exception exception) => _exception = exception;
            public Task<TResult> Enqueue<TResult>(Func<object?, CancellationToken, ValueTask<TResult>> taskFunc, object? state, CancellationToken cancellationToken)
                => Task.FromException<TResult>(_exception);
        }
    }
}
