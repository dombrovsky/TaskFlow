namespace TaskFlow.Extensions.Microsoft.Logging.Tests
{
    using global::Microsoft.Extensions.Logging;
    using NUnit.Framework;
    using System.Threading.Tasks.Flow;

    [TestFixture]
    internal sealed class LoggingTaskSchedulerExtensionsFixture
    {
        private const int EnqueuedEventId = 0x5446_0001;
        private const int StartedEventId = 0x5446_0002;
        private const int CancellationRequestedEventId = 0x5446_0003;
        private const int SucceededEventId = 0x5446_0004;
        private const int FailedEventId = 0x5446_0005;
        private const int FinishedEventId = 0x5446_0006;
        private TaskFlow? _taskFlow;
        [TearDown]
        public async Task TearDown()
        {
            if (_taskFlow != null)
            {
                await _taskFlow.DisposeAsync().ConfigureAwait(false);
                _taskFlow = null;
            }
        }

        [Test]
        public async Task WithLogging_LogsFullLifecycleAtTraceByDefault()
        {
            _taskFlow = new TaskFlow();
            var logger = new RecordingLogger(LogLevel.Trace);
            Assert.That(await _taskFlow.WithOperationName("answer").WithLogging(logger).Enqueue(() => 42), Is.EqualTo(42));
            Assert.That(logger.Entries.Select(x => x.EventId.Id), Is.EqualTo(new[] { EnqueuedEventId, StartedEventId, SucceededEventId, FinishedEventId }));
            Assert.That(logger.Entries, Has.All.Property(nameof(LogEntry.Level)).EqualTo(LogLevel.Trace));
            Assert.That(logger.Entries.Select(x => x.Message), Has.All.Contains("operation 1"));
            Assert.That(logger.Entries.Select(x => x.Message), Has.All.Contains("answer"));
        }

        [Test]
        public void WithLogging_LogsFailureWithException()
        {
            _taskFlow = new TaskFlow();
            var logger = new RecordingLogger(LogLevel.Trace);
            var task = _taskFlow.WithLogging(logger).Enqueue(() => throw new InvalidOperationException("boom"));
            Assert.That(async () => await task, Throws.InvalidOperationException);
            Assert.That(logger.Entries.Select(x => x.EventId.Id), Is.EqualTo(new[] { EnqueuedEventId, StartedEventId, FailedEventId, FinishedEventId }));
            Assert.That(logger.Entries.Single(x => x.EventId.Id == FailedEventId).Exception, Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public async Task WithLogging_ChecksEnabledLevelBeforeLogging()
        {
            _taskFlow = new TaskFlow();
            var logger = new RecordingLogger(LogLevel.Warning);
            await _taskFlow.WithLogging(logger).Enqueue(() => 42);
            Assert.That(logger.Entries, Is.Empty);
            Assert.That(logger.IsEnabledCalls, Is.GreaterThanOrEqualTo(6));
        }

        [Test]
        public async Task WithLogging_UsesConfiguredLevelsAndCustomAsyncInterceptor()
        {
            _taskFlow = new TaskFlow();
            var logger = new RecordingLogger(LogLevel.Information);
            var interceptor = new SuccessInterceptor();
            var scheduler = _taskFlow
                .WithLogging(logger, options =>
                {
                    options.EnqueuedLogLevel = LogLevel.Information;
                    options.SucceededLogLevel = LogLevel.Information;
                })
                .Intercept(interceptor);
            await scheduler.Enqueue(() => 42);
            Assert.That(logger.Entries.Select(x => x.EventId.Id), Is.EqualTo(new[] { EnqueuedEventId, SucceededEventId }));
            Assert.That(interceptor.Result, Is.EqualTo(42));
        }

        [Test]
        public void WithLogging_LogsCancellationRequestAndCanceledOutcome()
        {
            _taskFlow = new TaskFlow();
            var logger = new RecordingLogger(LogLevel.Trace);
            using var cts = new CancellationTokenSource();
            var task = _taskFlow.WithLogging(logger).Enqueue(token => Task.Delay(TimeSpan.FromSeconds(5), token), cts.Token);

            cts.Cancel();

            Assert.That(async () => await task, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(logger.Entries.Select(x => x.EventId.Id), Does.Contain(CancellationRequestedEventId));
            Assert.That(logger.Entries.Select(x => x.EventId.Id), Does.Contain(FailedEventId));
            Assert.That(logger.Entries.Select(x => x.EventId.Id), Does.Contain(FinishedEventId));
        }

        private sealed class SuccessInterceptor : IAsyncTaskSchedulerInterceptor, IAsyncTaskInterceptor
        {
            public object? Result { get; private set; }
            public IAsyncTaskInterceptor CreateInterceptor(TaskSchedulerInterceptionContext context) => this;
            public ValueTask OnBeforeAsync(TaskSchedulerInterceptionContext context) => default;
            public async ValueTask OnSuccessAsync<TResult>(TaskSchedulerInterceptionContext context, TResult result) { await Task.Yield(); Result = result; }
            public ValueTask OnErrorAsync(TaskSchedulerInterceptionContext context, Exception exception) => default;
            public ValueTask OnFinallyAsync(TaskSchedulerInterceptionContext context) => default;
        }

        private sealed class RecordingLogger : ILogger
        {
            private readonly LogLevel _minimumLevel;
            public RecordingLogger(LogLevel minimumLevel) => _minimumLevel = minimumLevel;
            public List<LogEntry> Entries { get; } = new List<LogEntry>();
            public int IsEnabledCalls { get; private set; }
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => EmptyDisposable.Instance;
            public bool IsEnabled(LogLevel logLevel) { IsEnabledCalls++; return logLevel >= _minimumLevel; }
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Entries.Add(new LogEntry(logLevel, eventId, exception, formatter(state, exception)));
        }
        private sealed class EmptyDisposable : IDisposable
        {
            public static EmptyDisposable Instance { get; } = new EmptyDisposable();
            public void Dispose() { }
        }
        private sealed record LogEntry(LogLevel Level, EventId EventId, Exception? Exception, string Message);
    }
}
