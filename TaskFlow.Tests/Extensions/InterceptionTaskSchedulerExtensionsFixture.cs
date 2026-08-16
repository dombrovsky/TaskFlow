namespace TaskFlow.Tests.Extensions
{
    using NUnit.Framework;
    using System.Threading.Tasks.Flow;

    [TestFixture]
    public sealed class InterceptionTaskSchedulerExtensionsFixture
    {
        private ITaskFlow? _taskFlow;
        [TearDown] public void TearDown() => _taskFlow?.Dispose(TimeSpan.FromSeconds(1));

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
            Assert.That(events, Is.EqualTo(new[] { "before", "operation", "success:42", "finally" }));
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
            Assert.That(events, Is.EqualTo(new[] { "before", "error:boom", "finally" }));
            Assert.That(interceptor.Context?.GetAnnotation<OperationNameAnnotation>()?.OperationName, Is.EqualTo("failure"));
            Assert.That(interceptor.Context?.State, Is.SameAs(state));
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public void Intercept_SuccessHookFailureDoesNotInvokeErrorHook(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;
            var interceptor = new ThrowingSuccessInterceptor();
            var task = taskFlow.Intercept(interceptor).Enqueue(() => 42);
            Assert.That(async () => await task, Throws.TypeOf<ApplicationException>());
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

        private sealed class RecordingInterceptor : ITaskSchedulerInterceptor
        {
            private readonly IList<string> _events;
            private readonly bool _asynchronous;
            public RecordingInterceptor(IList<string> events, bool asynchronous) { _events = events; _asynchronous = asynchronous; }
            public TaskSchedulerInterceptionContext? Context { get; private set; }
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

        private sealed class ThrowingSuccessInterceptor : ITaskSchedulerInterceptor
        {
            public bool ErrorCalled { get; private set; }
            public ValueTask OnBeforeAsync(TaskSchedulerInterceptionContext context) => default;
            public ValueTask OnSuccessAsync<TResult>(TaskSchedulerInterceptionContext context, TResult result) => throw new ApplicationException();
            public ValueTask OnErrorAsync(TaskSchedulerInterceptionContext context, Exception exception) { ErrorCalled = true; return default; }
            public ValueTask OnFinallyAsync(TaskSchedulerInterceptionContext context) => default;
        }

        private sealed class DelegateInterceptor : ITaskSchedulerInterceptor
        {
            private readonly Func<ValueTask> _before;
            public DelegateInterceptor(Func<ValueTask> before) => _before = before;
            public ValueTask OnBeforeAsync(TaskSchedulerInterceptionContext context) => _before();
            public ValueTask OnSuccessAsync<TResult>(TaskSchedulerInterceptionContext context, TResult result) => default;
            public ValueTask OnErrorAsync(TaskSchedulerInterceptionContext context, Exception exception) => default;
            public ValueTask OnFinallyAsync(TaskSchedulerInterceptionContext context) => default;
        }

        private sealed class BeforeFailureInterceptor : ITaskSchedulerInterceptor
        {
            public int FinallyCallCount { get; private set; }
            public ValueTask OnBeforeAsync(TaskSchedulerInterceptionContext context) => throw new InvalidOperationException();
            public ValueTask OnSuccessAsync<TResult>(TaskSchedulerInterceptionContext context, TResult result) => default;
            public ValueTask OnErrorAsync(TaskSchedulerInterceptionContext context, Exception exception) => default;
            public ValueTask OnFinallyAsync(TaskSchedulerInterceptionContext context)
            {
                FinallyCallCount++;
                return default;
            }
        }

        private sealed class ContextRecordingInterceptor : ITaskSchedulerInterceptor
        {
            public SynchronizationContext? BeforeContext { get; private set; }
            public SynchronizationContext? SuccessContext { get; private set; }
            public SynchronizationContext? ErrorContext { get; private set; }
            public SynchronizationContext? FinallyContext { get; private set; }

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

        private sealed class ThreadRecordingInterceptor : ITaskSchedulerInterceptor
        {
            public int BeforeThreadId { get; private set; }
            public int SuccessThreadId { get; private set; }
            public int FinallyThreadId { get; private set; }

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
