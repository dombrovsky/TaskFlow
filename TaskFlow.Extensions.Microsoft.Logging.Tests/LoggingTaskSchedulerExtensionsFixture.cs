namespace TaskFlow.Extensions.Microsoft.Logging.Tests
{
    using global::Microsoft.Extensions.Logging;
    using NUnit.Framework;
    using System.Threading.Tasks.Flow;

    [TestFixture]
    public sealed class LoggingTaskSchedulerExtensionsFixture
    {
        private TaskFlow? _taskFlow;
        [TearDown] public void TearDown() => _taskFlow?.Dispose(TimeSpan.FromSeconds(1));

        [Test]
        public async Task WithLogging_LogsFullLifecycleAtTraceByDefault()
        {
            _taskFlow = new TaskFlow();
            var logger = new RecordingLogger(LogLevel.Trace);
            Assert.That(await _taskFlow.WithLogging(logger).WithOperationName("answer").Enqueue(() => 42), Is.EqualTo(42));
            Assert.That(logger.Entries.Select(x => x.EventId.Id), Is.EqualTo(new[] { 1, 2, 4, 6 }));
            Assert.That(logger.Entries, Has.All.Property(nameof(LogEntry.Level)).EqualTo(LogLevel.Trace));
            Assert.That(logger.Entries.Skip(1).Select(x => x.Message), Has.All.Contains("answer"));
        }

        [Test]
        public void WithLogging_LogsFailureWithException()
        {
            _taskFlow = new TaskFlow();
            var logger = new RecordingLogger(LogLevel.Trace);
            var task = _taskFlow.WithLogging(logger).Enqueue(() => throw new InvalidOperationException("boom"));
            Assert.That(async () => await task, Throws.InvalidOperationException);
            Assert.That(logger.Entries.Select(x => x.EventId.Id), Is.EqualTo(new[] { 1, 2, 5, 6 }));
            Assert.That(logger.Entries.Single(x => x.EventId.Id == 5).Exception, Is.TypeOf<InvalidOperationException>());
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
            var scheduler = _taskFlow.WithLogging(logger, options =>
            {
                options.EnqueuedLogLevel = LogLevel.Information;
                options.SucceededLogLevel = LogLevel.Information;
                options.Interceptor = interceptor;
            });
            await scheduler.Enqueue(() => 42);
            Assert.That(logger.Entries.Select(x => x.EventId.Id), Is.EqualTo(new[] { 1, 4 }));
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
            Assert.That(logger.Entries.Select(x => x.EventId.Id), Does.Contain(3));
            Assert.That(logger.Entries.Select(x => x.EventId.Id), Does.Contain(5));
            Assert.That(logger.Entries.Select(x => x.EventId.Id), Does.Contain(6));
        }

        private sealed class SuccessInterceptor : ITaskSchedulerInterceptor
        {
            public object? Result { get; private set; }
            public ValueTask OnBeforeAsync(TaskSchedulerInterceptionContext context) => default;
            public async ValueTask OnSuccessAsync<TResult>(TaskSchedulerInterceptionContext context, TResult result) { await Task.Yield(); Result = result; }
            public ValueTask OnErrorAsync(TaskSchedulerInterceptionContext context, Exception exception) => default;
        }

        private sealed class RecordingLogger : ILogger
        {
            private readonly LogLevel _minimumLevel;
            public RecordingLogger(LogLevel minimumLevel) => _minimumLevel = minimumLevel;
            public List<LogEntry> Entries { get; } = new List<LogEntry>();
            public int IsEnabledCalls { get; private set; }
            public IDisposable BeginScope<TState>(TState state) => EmptyDisposable.Instance;
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
