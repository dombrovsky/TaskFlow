namespace TaskFlow.Tests.Extensions
{
    using NUnit.Framework;
    using System.Threading.Tasks.Flow;

    [TestFixture]
    internal sealed class InterceptionTaskSchedulerExtensionsFixture
    {
        private static readonly string[] SuccessfulLifecycle = ["before", "operation", "success:42", "finally"];
        private static readonly string[] FailedLifecycle = ["before", "error:boom", "finally"];
        private static readonly int[] SuccessfulResults = [1, 2];
        private static readonly string[] SuccessfulHookLifecycle = ["before", "success:42", "finally"];
        private static readonly string[] FailedBeforeLifecycle = ["before", "finally"];
        private ITaskFlow? _taskFlow;
        [TearDown]
        public async Task TearDown()
        {
            if (_taskFlow != null)
            {
                await _taskFlow.DisposeAsync().ConfigureAwait(false);
                _taskFlow = null;
            }
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public async Task Intercept_AwaitsAsynchronousHooksInOrder(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;
            var events = new List<string>();
            var interceptor = new RecordingInterceptor(events, true);
            var result = await taskFlow.Intercept(interceptor).Enqueue(async () =>
            {
                events.Add("operation");
                await Task.Yield();
                return 42;
            });
            Assert.That(result, Is.EqualTo(42));
            Assert.That(events, Is.EqualTo(SuccessfulLifecycle));
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public void Intercept_ObservesErrorsAndMetadata(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;
            var events = new List<string>();
            var interceptor = new RecordingInterceptor(events, false);
            var state = new object();
            Func<object?, CancellationToken, ValueTask<int>> operation = (_, _) => throw new InvalidOperationException("boom");
            var task = taskFlow.Intercept(interceptor).WithOperationName("failure")
                .Enqueue(operation, state, CancellationToken.None);
            Assert.That(async () => await task, Throws.InvalidOperationException.With.Message.EqualTo("boom"));
            Assert.That(events, Is.EqualTo(FailedLifecycle));
            Assert.That(interceptor.Context?.GetAnnotation<OperationNameAnnotation>()?.OperationName, Is.EqualTo("failure"));
            Assert.That(interceptor.Context?.State, Is.SameAs(state));
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public void Intercept_SuccessHookFailureDoesNotInvokeErrorHook(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;
            var interceptor = new ThrowingSuccessInterceptor();
            var task = taskFlow.Intercept(interceptor).Enqueue(() => 42);
            Assert.That(async () => await task, Throws.TypeOf<NotSupportedException>());
            Assert.That(interceptor.ErrorCalled, Is.False);
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public async Task Intercept_AsynchronousBeforeHookPreservesSchedulerContext(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;
            SynchronizationContext? hookContext = null;
            SynchronizationContext? operationContext = null;
            var interceptor = new DelegateInterceptor(async () =>
            {
                await Task.Yield();
                hookContext = SynchronizationContext.Current;
            });

            await taskFlow.Intercept(interceptor).Enqueue(() => operationContext = SynchronizationContext.Current);

            Assert.That(operationContext, Is.SameAs(hookContext));
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public async Task Intercept_AllSuccessHooksCaptureOperationContext(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;
            var interceptor = new ContextRecordingInterceptor();
            SynchronizationContext? operationContext = null;

            await taskFlow.Intercept(interceptor).Enqueue(async () =>
            {
                await Task.Yield();
                operationContext = SynchronizationContext.Current;
            });

            Assert.That(interceptor.BeforeContext, Is.SameAs(operationContext));
            Assert.That(interceptor.SuccessContext, Is.SameAs(operationContext));
            Assert.That(interceptor.FinallyContext, Is.SameAs(operationContext));
            Assert.That(interceptor.ErrorContext, Is.Null);
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public void Intercept_ErrorAndFinallyHooksCaptureOperationContext(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;
            var interceptor = new ContextRecordingInterceptor();
            SynchronizationContext? operationContext = null;
            var task = taskFlow.Intercept(interceptor).Enqueue(async () =>
            {
                await Task.Yield();
                operationContext = SynchronizationContext.Current;
                throw new InvalidOperationException();
            });

            Assert.That(async () => await task, Throws.TypeOf<InvalidOperationException>());
            Assert.That(interceptor.BeforeContext, Is.SameAs(operationContext));
            Assert.That(interceptor.ErrorContext, Is.SameAs(operationContext));
            Assert.That(interceptor.FinallyContext, Is.SameAs(operationContext));
            Assert.That(interceptor.SuccessContext, Is.Null);
        }

        [Test]
        public async Task Intercept_DedicatedThread_AsynchronousHooksAndOperationStayOnFlowThread()
        {
            _taskFlow = new DedicatedThreadTaskFlow();
            var interceptor = new ThreadRecordingInterceptor();
            var operationThreadId = 0;

            await _taskFlow.Intercept(interceptor).Enqueue(async () =>
            {
                await Task.Yield();
                operationThreadId = Environment.CurrentManagedThreadId;
            });

            Assert.That(interceptor.BeforeThreadId, Is.EqualTo(operationThreadId));
            Assert.That(interceptor.SuccessThreadId, Is.EqualTo(operationThreadId));
            Assert.That(interceptor.FinallyThreadId, Is.EqualTo(operationThreadId));
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public void Intercept_FinallyHookRunsWhenBeforeHookFails(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;
            var interceptor = new BeforeFailureInterceptor();

            var task = taskFlow.Intercept(interceptor).Enqueue(() => 42);

            Assert.That(async () => await task, Throws.TypeOf<InvalidOperationException>());
            Assert.That(interceptor.FinallyCallCount, Is.EqualTo(1));
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public async Task Intercept_SynchronousStructMaintainsPerOperationState(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;
            var events = new List<string>();

            var scheduler = taskFlow.Intercept(new SynchronousRecordingInterceptor(events));
            var results = await Task.WhenAll(scheduler.Enqueue(() => 1), scheduler.Enqueue(() => 2));

            Assert.That(results, Is.EqualTo(SuccessfulResults));
            Assert.That(events.Count(x => x == "before:1"), Is.EqualTo(2));
            Assert.That(events, Does.Contain("success:1:1"));
            Assert.That(events, Does.Contain("success:1:2"));
            Assert.That(events.Count(x => x == "finally:1"), Is.EqualTo(2));
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public async Task Intercept_SynchronousHooksRunInOrderAndObserveContext(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;
            var recorder = new SynchronousLifecycleRecorder();
            var state = new object();
            using var cts = new CancellationTokenSource();

            var result = await taskFlow.Intercept(new SynchronousLifecycleInterceptor(recorder))
                .WithOperationName("sync")
                .Enqueue((operationState, token) =>
                {
                    recorder.Events.Add("operation");
                    Assert.That(operationState, Is.SameAs(state));
                    Assert.That(token.CanBeCanceled, Is.True);
                    return new ValueTask<int>(42);
                }, state, cts.Token);

            Assert.That(result, Is.EqualTo(42));
            Assert.That(recorder.Events, Is.EqualTo(SuccessfulLifecycle));
            Assert.That(recorder.Context.State, Is.SameAs(state));
            Assert.That(recorder.Context.CancellationToken, Is.EqualTo(cts.Token));
            Assert.That(recorder.Context.GetAnnotation<OperationNameAnnotation>()?.OperationName, Is.EqualTo("sync"));
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public void Intercept_SynchronousHooksObserveOperationError(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;
            var recorder = new SynchronousLifecycleRecorder();
            Func<ValueTask<int>> operation = () => throw new InvalidOperationException("boom");

            var task = taskFlow.Intercept(new SynchronousLifecycleInterceptor(recorder)).Enqueue(operation);

            Assert.That(async () => await task, Throws.InvalidOperationException.With.Message.EqualTo("boom"));
            Assert.That(recorder.Events, Is.EqualTo(FailedLifecycle));
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public void Intercept_SynchronousSuccessFailureDoesNotInvokeError(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;
            var recorder = new SynchronousLifecycleRecorder { ThrowOnSuccess = true };

            var task = taskFlow.Intercept(new SynchronousLifecycleInterceptor(recorder)).Enqueue(() => 42);

            Assert.That(async () => await task, Throws.TypeOf<NotSupportedException>());
            Assert.That(recorder.Events, Is.EqualTo(SuccessfulHookLifecycle));
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public void Intercept_SynchronousFinallyRunsWhenBeforeFails(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;
            var recorder = new SynchronousLifecycleRecorder { ThrowOnBefore = true };
            var operationCalled = false;

            var task = taskFlow.Intercept(new SynchronousLifecycleInterceptor(recorder)).Enqueue(() => operationCalled = true);

            Assert.That(async () => await task, Throws.TypeOf<InvalidOperationException>());
            Assert.That(operationCalled, Is.False);
            Assert.That(recorder.Events, Is.EqualTo(FailedBeforeLifecycle));
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public async Task Intercept_SynchronousHooksPreserveSchedulerContext(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;
            var recorder = new SynchronousLifecycleRecorder();
            SynchronizationContext? operationContext = null;

            await taskFlow.Intercept(new SynchronousLifecycleInterceptor(recorder)).Enqueue(async () =>
            {
                await Task.Yield();
                operationContext = SynchronizationContext.Current;
            });

            Assert.That(recorder.BeforeContext, Is.SameAs(operationContext));
            Assert.That(recorder.SuccessContext, Is.SameAs(operationContext));
            Assert.That(recorder.FinallyContext, Is.SameAs(operationContext));
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public async Task Intercept_AsynchronousFactoryCreatesInterceptorPerOperation(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;
            var events = new List<string>();
            var factory = new PerOperationAsyncInterceptorFactory(events);
            var scheduler = taskFlow.Intercept(factory);

            await Task.WhenAll(scheduler.Enqueue(() => 1), scheduler.Enqueue(() => 2));

            Assert.That(factory.CreatedCount, Is.EqualTo(2));
            Assert.That(events, Does.Contain("before:1"));
            Assert.That(events, Does.Contain("success:1:1"));
            Assert.That(events, Does.Contain("finally:1"));
            Assert.That(events, Does.Contain("before:2"));
            Assert.That(events, Does.Contain("success:2:2"));
            Assert.That(events, Does.Contain("finally:2"));
        }

        private sealed class PerOperationAsyncInterceptorFactory : IAsyncTaskSchedulerInterceptor
        {
            private readonly IList<string> _events;
            private int _createdCount;

            public PerOperationAsyncInterceptorFactory(IList<string> events) => _events = events;

            public int CreatedCount => _createdCount;

            public IAsyncTaskInterceptor CreateInterceptor(TaskSchedulerInterceptionContext context)
            {
                return new PerOperationAsyncInterceptor(Interlocked.Increment(ref _createdCount), _events);
            }
        }

        private sealed class PerOperationAsyncInterceptor : IAsyncTaskInterceptor
        {
            private readonly int _id;
            private readonly IList<string> _events;

            public PerOperationAsyncInterceptor(int id, IList<string> events)
            {
                _id = id;
                _events = events;
            }

            public ValueTask OnBeforeAsync(TaskSchedulerInterceptionContext context)
            {
                _events.Add($"before:{_id}");
                return default;
            }

            public ValueTask OnSuccessAsync<TResult>(TaskSchedulerInterceptionContext context, TResult result)
            {
                _events.Add($"success:{_id}:{result}");
                return default;
            }

            public ValueTask OnErrorAsync(TaskSchedulerInterceptionContext context, Exception exception)
            {
                _events.Add($"error:{_id}");
                return default;
            }

            public ValueTask OnFinallyAsync(TaskSchedulerInterceptionContext context)
            {
                _events.Add($"finally:{_id}");
                return default;
            }
        }

        private struct SynchronousRecordingInterceptor : ITaskSchedulerInterceptor
        {
            private readonly IList<string> _events;
            private int _stage;

            public SynchronousRecordingInterceptor(IList<string> events)
            {
                _events = events;
                _stage = 0;
            }

            public void OnBefore(TaskSchedulerInterceptionContext context)
            {
                _stage++;
                _events.Add($"before:{_stage}");
            }

            public void OnSuccess<TResult>(TaskSchedulerInterceptionContext context, TResult result) => _events.Add($"success:{_stage}:{result}");
            public void OnError(TaskSchedulerInterceptionContext context, Exception exception) => _events.Add($"error:{_stage}");
            public void OnFinally(TaskSchedulerInterceptionContext context) => _events.Add($"finally:{_stage}");
        }

        private sealed class SynchronousLifecycleRecorder
        {
            public List<string> Events { get; } = new List<string>();
            public TaskSchedulerInterceptionContext Context { get; set; }
            public SynchronizationContext? BeforeContext { get; set; }
            public SynchronizationContext? SuccessContext { get; set; }
            public SynchronizationContext? FinallyContext { get; set; }
            public bool ThrowOnBefore { get; set; }
            public bool ThrowOnSuccess { get; set; }
        }

        private readonly struct SynchronousLifecycleInterceptor : ITaskSchedulerInterceptor
        {
            private readonly SynchronousLifecycleRecorder _recorder;

            public SynchronousLifecycleInterceptor(SynchronousLifecycleRecorder recorder) => _recorder = recorder;

            public void OnBefore(TaskSchedulerInterceptionContext context)
            {
                _recorder.Context = context;
                _recorder.BeforeContext = SynchronizationContext.Current;
                _recorder.Events.Add("before");
                if (_recorder.ThrowOnBefore) throw new InvalidOperationException();
            }

            public void OnSuccess<TResult>(TaskSchedulerInterceptionContext context, TResult result)
            {
                _recorder.SuccessContext = SynchronizationContext.Current;
                _recorder.Events.Add($"success:{result}");
                if (_recorder.ThrowOnSuccess) throw new NotSupportedException();
            }

            public void OnError(TaskSchedulerInterceptionContext context, Exception exception) => _recorder.Events.Add($"error:{exception.Message}");

            public void OnFinally(TaskSchedulerInterceptionContext context)
            {
                _recorder.FinallyContext = SynchronizationContext.Current;
                _recorder.Events.Add("finally");
            }
        }

        private sealed class RecordingInterceptor : IAsyncTaskSchedulerInterceptor, IAsyncTaskInterceptor
        {
            private readonly IList<string> _events;
            private readonly bool _asynchronous;
            public RecordingInterceptor(IList<string> events, bool asynchronous) { _events = events; _asynchronous = asynchronous; }
            public TaskSchedulerInterceptionContext? Context { get; private set; }
            public IAsyncTaskInterceptor CreateInterceptor(TaskSchedulerInterceptionContext context) => this;
            public ValueTask OnBeforeAsync(TaskSchedulerInterceptionContext context) { Context = context; return RecordAsync("before"); }
            public ValueTask OnSuccessAsync<TResult>(TaskSchedulerInterceptionContext context, TResult result) => RecordAsync($"success:{result}");
            public ValueTask OnErrorAsync(TaskSchedulerInterceptionContext context, Exception exception) => RecordAsync($"error:{exception.Message}");
            public ValueTask OnFinallyAsync(TaskSchedulerInterceptionContext context) => RecordAsync("finally");
            private ValueTask RecordAsync(string value)
            {
                if (!_asynchronous) { _events.Add(value); return default; }
                return new ValueTask(RecordLaterAsync(value));
            }
            private async Task RecordLaterAsync(string value) { await Task.Yield(); _events.Add(value); }
        }

        private sealed class ThrowingSuccessInterceptor : IAsyncTaskSchedulerInterceptor, IAsyncTaskInterceptor
        {
            public bool ErrorCalled { get; private set; }
            public IAsyncTaskInterceptor CreateInterceptor(TaskSchedulerInterceptionContext context) => this;
            public ValueTask OnBeforeAsync(TaskSchedulerInterceptionContext context) => default;
            public ValueTask OnSuccessAsync<TResult>(TaskSchedulerInterceptionContext context, TResult result) => throw new NotSupportedException();
            public ValueTask OnErrorAsync(TaskSchedulerInterceptionContext context, Exception exception) { ErrorCalled = true; return default; }
            public ValueTask OnFinallyAsync(TaskSchedulerInterceptionContext context) => default;
        }

        private sealed class DelegateInterceptor : IAsyncTaskSchedulerInterceptor, IAsyncTaskInterceptor
        {
            private readonly Func<ValueTask> _before;
            public DelegateInterceptor(Func<ValueTask> before) => _before = before;
            public IAsyncTaskInterceptor CreateInterceptor(TaskSchedulerInterceptionContext context) => this;
            public ValueTask OnBeforeAsync(TaskSchedulerInterceptionContext context) => _before();
            public ValueTask OnSuccessAsync<TResult>(TaskSchedulerInterceptionContext context, TResult result) => default;
            public ValueTask OnErrorAsync(TaskSchedulerInterceptionContext context, Exception exception) => default;
            public ValueTask OnFinallyAsync(TaskSchedulerInterceptionContext context) => default;
        }

        private sealed class BeforeFailureInterceptor : IAsyncTaskSchedulerInterceptor, IAsyncTaskInterceptor
        {
            public int FinallyCallCount { get; private set; }
            public IAsyncTaskInterceptor CreateInterceptor(TaskSchedulerInterceptionContext context) => this;
            public ValueTask OnBeforeAsync(TaskSchedulerInterceptionContext context) => throw new InvalidOperationException();
            public ValueTask OnSuccessAsync<TResult>(TaskSchedulerInterceptionContext context, TResult result) => default;
            public ValueTask OnErrorAsync(TaskSchedulerInterceptionContext context, Exception exception) => default;
            public ValueTask OnFinallyAsync(TaskSchedulerInterceptionContext context)
            {
                FinallyCallCount++;
                return default;
            }
        }

        private sealed class ContextRecordingInterceptor : IAsyncTaskSchedulerInterceptor, IAsyncTaskInterceptor
        {
            public SynchronizationContext? BeforeContext { get; private set; }
            public SynchronizationContext? SuccessContext { get; private set; }
            public SynchronizationContext? ErrorContext { get; private set; }
            public SynchronizationContext? FinallyContext { get; private set; }

            public IAsyncTaskInterceptor CreateInterceptor(TaskSchedulerInterceptionContext context) => this;

            public async ValueTask OnBeforeAsync(TaskSchedulerInterceptionContext context)
            {
                await Task.Yield();
                BeforeContext = SynchronizationContext.Current;
            }

            public async ValueTask OnSuccessAsync<TResult>(TaskSchedulerInterceptionContext context, TResult result)
            {
                await Task.Yield();
                SuccessContext = SynchronizationContext.Current;
            }

            public async ValueTask OnErrorAsync(TaskSchedulerInterceptionContext context, Exception exception)
            {
                await Task.Yield();
                ErrorContext = SynchronizationContext.Current;
            }

            public async ValueTask OnFinallyAsync(TaskSchedulerInterceptionContext context)
            {
                await Task.Yield();
                FinallyContext = SynchronizationContext.Current;
            }
        }

        private sealed class ThreadRecordingInterceptor : IAsyncTaskSchedulerInterceptor, IAsyncTaskInterceptor
        {
            public int BeforeThreadId { get; private set; }
            public int SuccessThreadId { get; private set; }
            public int FinallyThreadId { get; private set; }

            public IAsyncTaskInterceptor CreateInterceptor(TaskSchedulerInterceptionContext context) => this;

            public async ValueTask OnBeforeAsync(TaskSchedulerInterceptionContext context)
            {
                await Task.Yield();
                BeforeThreadId = Environment.CurrentManagedThreadId;
            }

            public async ValueTask OnSuccessAsync<TResult>(TaskSchedulerInterceptionContext context, TResult result)
            {
                await Task.Yield();
                SuccessThreadId = Environment.CurrentManagedThreadId;
            }

            public ValueTask OnErrorAsync(TaskSchedulerInterceptionContext context, Exception exception) => default;

            public async ValueTask OnFinallyAsync(TaskSchedulerInterceptionContext context)
            {
                await Task.Yield();
                FinallyThreadId = Environment.CurrentManagedThreadId;
            }
        }
    }
}
