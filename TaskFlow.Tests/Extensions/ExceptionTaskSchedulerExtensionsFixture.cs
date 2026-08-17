namespace TaskFlow.Tests.Extensions
{
    using NUnit.Framework;
    using System.Threading.Tasks.Flow;
    using System.Threading.Tasks.Flow.Annotations;

    [TestFixture]
    internal sealed class ExceptionTaskSchedulerExtensionsFixture
    {
        private static readonly string[] FooAndGeneric = ["foo", "generic"];
        private static readonly string[] BarAndGeneric = ["bar", "generic"];
        private static readonly string[] Generic = ["generic"];
        private static readonly string[] InnerAndOuter = ["inner", "outer"];
        private static readonly string[] InterceptorInnerAndOuter = ["interceptor", "finally", "inner", "outer"];
        private ITaskFlow? _taskFlow;

        [TearDown]
        public void TearDown()
        {
            _taskFlow?.Dispose(TimeSpan.FromSeconds(1));
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public void Enqueue_ShouldExecuteHandler_IfExceptionOfThatTypeOccurred(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;

            var exceptions = new List<Exception>();
            var task = taskFlow
                .OnError<InvalidOperationException>(exceptions.Add)
                .Enqueue(() => throw new InvalidOperationException());

            Assert.That(async () => await task.ConfigureAwait(false), Throws.InstanceOf<InvalidOperationException>());
            Assert.That(exceptions, Has.One.TypeOf<InvalidOperationException>());
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public void Enqueue_ShouldNotExecuteHandler_IfExceptionOfAnotherTypeOccurred(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;

            var exceptions = new List<Exception>();
            var task = taskFlow
                .OnError<InvalidOperationException>(exceptions.Add)
                .Enqueue(() => throw new ArgumentException("Expected test failure"));

            Assert.That(async () => await task.ConfigureAwait(false), Throws.InstanceOf<ArgumentException>());
            Assert.That(exceptions, Is.Empty);
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public void Enqueue_ShouldExecuteHandler_IfExceptionMatchingFilter(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;

            var exceptions = new List<Exception>();
            var task = taskFlow
                .OnError<InvalidOperationException>(exceptions.Add, exception => exception.Message.Contains("foo", StringComparison.Ordinal))
                .Enqueue(() => throw new InvalidOperationException("foo"));

            Assert.That(async () => await task.ConfigureAwait(false), Throws.InstanceOf<InvalidOperationException>());
            Assert.That(exceptions, Has.One.TypeOf<InvalidOperationException>());
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public void Enqueue_ShouldNotExecuteHandler_IfExceptionNotMatchingFilter(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;

            var exceptions = new List<Exception>();
            var task = taskFlow
                .OnError<InvalidOperationException>(exceptions.Add, exception => exception.Message.Contains("bar", StringComparison.Ordinal))
                .Enqueue(() => throw new InvalidOperationException("foo"));

            Assert.That(async () => await task.ConfigureAwait(false), Throws.InstanceOf<InvalidOperationException>());
            Assert.That(exceptions, Is.Empty);
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public void Enqueue_MultipleHandlers_ShouldExecuteAllMatchingHandlers(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;

            var exceptions = new List<string>();
            var errorHandlingScheduler = taskFlow
                .OnError<InvalidOperationException>(_ => exceptions.Add("foo"), exception => exception.Message.Contains("foo", StringComparison.Ordinal))
                .OnError<InvalidOperationException>(_ => exceptions.Add("bar"), exception => exception.Message.Contains("bar", StringComparison.Ordinal))
                .OnError(_ => exceptions.Add("generic"));

            var task1 = errorHandlingScheduler.Enqueue(() => throw new InvalidOperationException("foo"));

            Assert.That(async () => await task1.ConfigureAwait(false), Throws.InstanceOf<InvalidOperationException>());
            Assert.That(exceptions, Is.EqualTo(FooAndGeneric));
            exceptions.Clear();

            var task2 = errorHandlingScheduler.Enqueue(() => throw new InvalidOperationException("bar"));
            Assert.That(async () => await task2.ConfigureAwait(false), Throws.InstanceOf<InvalidOperationException>());
            Assert.That(exceptions, Is.EqualTo(BarAndGeneric));
            exceptions.Clear();

            var task3 = errorHandlingScheduler.Enqueue(() => throw new InvalidOperationException());
            Assert.That(async () => await task3.ConfigureAwait(false), Throws.InstanceOf<InvalidOperationException>());
            Assert.That(exceptions, Is.EqualTo(Generic));
            exceptions.Clear();

            var task4 = errorHandlingScheduler.Enqueue(() => throw new ArgumentException("Expected test failure"));
            Assert.That(async () => await task4.ConfigureAwait(false), Throws.InstanceOf<ArgumentException>());
            Assert.That(exceptions, Is.EqualTo(Generic));
            exceptions.Clear();
        }

        [Test]
        public void OnError_DedicatedThread_AsynchronousFailureCallbackRetainsFlowContext()
        {
            var flow = new DedicatedThreadTaskFlow();
            _taskFlow = flow;
            var operationThread = 0;
            var callbackThread = 0;
            SynchronizationContext? operationContext = null;
            SynchronizationContext? callbackContext = null;

            var task = flow.OnError<InvalidOperationException>(_ =>
                {
                    callbackThread = Environment.CurrentManagedThreadId;
                    callbackContext = SynchronizationContext.Current;
                })
                .Enqueue(FailAsync);

            Assert.That(async () => await task, Throws.InvalidOperationException);
            Assert.That(callbackThread, Is.EqualTo(operationThread));
            Assert.That(callbackContext, Is.SameAs(operationContext).And.Not.Null);

            async ValueTask<int> FailAsync(CancellationToken _)
            {
                await Task.Yield();
                operationThread = Environment.CurrentManagedThreadId;
                operationContext = SynchronizationContext.Current;
                throw new InvalidOperationException("expected");
            }
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public void Enqueue_InterleavedHandlers_ShouldExecuteInRegistrationOrder(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;
            var handlers = new List<string>();
            var scheduler = taskFlow
                .OnError<InvalidOperationException>(_ => handlers.Add("inner"))
                .WithOperationName("failing-operation")
                .OnError<Exception>(_ => handlers.Add("outer"));

            var task = scheduler.Enqueue(() => throw new InvalidOperationException("Expected test failure"));

            Assert.That(async () => await task.ConfigureAwait(false), Throws.InvalidOperationException);
            Assert.That(handlers, Is.EqualTo(InnerAndOuter));
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public void Enqueue_HandlersInterleavedWithInterceptor_ShouldExecuteAfterInterceptorInRegistrationOrder(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;
            var events = new List<string>();
            var scheduler = taskFlow
                .OnError<InvalidOperationException>(_ => events.Add("inner"))
                .Intercept(new ErrorRecordingInterceptor(events))
                .OnError<Exception>(_ => events.Add("outer"));

            var task = scheduler.Enqueue(() => throw new InvalidOperationException("Expected test failure"));

            Assert.That(async () => await task.ConfigureAwait(false), Throws.InvalidOperationException);
            Assert.That(events, Is.EqualTo(InterceptorInnerAndOuter));
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public void Enqueue_AsynchronouslyFailingOperation_ShouldExecuteHandlerAndPropagateOriginalException(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;
            var expectedException = new InvalidOperationException("Expected test failure");
            Exception? handledException = null;

            var task = taskFlow
                .OnError<InvalidOperationException>(exception => handledException = exception)
                .Enqueue(FailAsync);

            var actualException = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await task.ConfigureAwait(false));
            Assert.That(actualException, Is.SameAs(expectedException));
            Assert.That(handledException, Is.SameAs(expectedException));

            async Task FailAsync()
            {
                await Task.Yield();
                throw expectedException;
            }
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public void OnError_CallbackFailureBecomesOutcomeForLaterHandlers(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;
            Exception? observed = null;
            var scheduler = taskFlow
                .OnError<InvalidOperationException>(_ => throw new NotSupportedException("replacement"))
                .Intercept(new NoOpInterceptor())
                .OnError<Exception>(exception => observed = exception);

            var task = scheduler.Enqueue(() => throw new InvalidOperationException("original"));

            Assert.That(async () => await task, Throws.TypeOf<NotSupportedException>().With.Message.EqualTo("replacement"));
            Assert.That(observed, Is.TypeOf<NotSupportedException>().With.Message.EqualTo("replacement"));
        }

        private readonly struct NoOpInterceptor : ITaskSchedulerInterceptor
        {
            public void OnBefore(TaskSchedulerInterceptionContext context) { }
            public void OnSuccess<TResult>(TaskSchedulerInterceptionContext context, TResult result) { }
            public void OnError(TaskSchedulerInterceptionContext context, Exception exception) { }
            public void OnFinally(TaskSchedulerInterceptionContext context) { }
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public void Enqueue_Handler_ShouldReceiveOperationAnnotation(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;
            OperationNameAnnotation? handledAnnotation = null;

            var task = taskFlow
                .WithOperationName("failing-operation")
                .OnError<InvalidOperationException>((_, _, annotation) => handledAnnotation = annotation)
                .Enqueue(() => throw new InvalidOperationException("Expected test failure"));

            Assert.That(async () => await task.ConfigureAwait(false), Throws.InvalidOperationException);
            Assert.That(handledAnnotation?.OperationName, Is.EqualTo("failing-operation"));
        }

        private readonly struct ErrorRecordingInterceptor : ITaskSchedulerInterceptor
        {
            private readonly IList<string> _events;

            public ErrorRecordingInterceptor(IList<string> events)
            {
                _events = events;
            }

            public void OnBefore(TaskSchedulerInterceptionContext context)
            {
            }

            public void OnSuccess<TResult>(TaskSchedulerInterceptionContext context, TResult result)
            {
            }

            public void OnError(TaskSchedulerInterceptionContext context, Exception exception)
            {
                _events.Add("interceptor");
            }

            public void OnFinally(TaskSchedulerInterceptionContext context)
            {
                _events.Add("finally");
            }
        }

    }
}
