namespace TaskFlow.Extensions.Microsoft.DependencyInjection.Tests
{
    using global::Microsoft.Extensions.DependencyInjection;
    using NUnit.Framework;
    using System.Threading.Tasks.Flow;

    [TestFixture]
    internal sealed class CustomTaskFlowServiceCollectionExtensionsFixture
    {
        [Test]
        public async Task AddTaskFlow_ShouldRegisterTaskFlowInfoAndTaskScheduler_UsingCustomFactory()
        {
            var factoryCalls = 0;

            using var container = new ServiceCollection()
                .AddTaskFlow(null, (provider, options) =>
                {
                    factoryCalls++;
                    return new StubTaskFlow(options);
                })
                .BuildServiceProvider();

            var taskFlowInfo = container.GetRequiredService<ITaskFlowInfo>();
            var taskScheduler = container.GetRequiredService<ITaskScheduler>();

            Assert.That(taskFlowInfo, Is.SameAs(taskScheduler));
            Assert.That(factoryCalls, Is.EqualTo(1));
            Assert.That(await taskScheduler.Enqueue(() => 42), Is.EqualTo(42));
        }

        [Test]
        public void AddTaskFlow_ShouldUseConfiguredOptionsForCustomFactory()
        {
            var expectedOptions = new TaskFlowOptions { SynchronousDisposeTimeout = TimeSpan.FromSeconds(7) };

            using var container = new ServiceCollection()
                .AddTaskFlow(
                    name: null,
                    baseTaskFlowFactory: (_, options) => new StubTaskFlow(options),
                    configureOptions: _ => expectedOptions)
                .BuildServiceProvider();

            var taskFlowInfo = container.GetRequiredService<ITaskFlowInfo>();
            Assert.That(taskFlowInfo.Options, Is.SameAs(expectedOptions));
        }

        [Test]
        public async Task AddTaskFlow_ShouldApplyConfiguredSchedulerChain()
        {
            var chainCalls = 0;
            var chainEnqueueCalls = 0;

            using var container = new ServiceCollection()
                .AddTaskFlow(
                    name: null,
                    baseTaskFlowFactory: (_, options) => new StubTaskFlow(options),
                    configureSchedulerChain: (scheduler, _) =>
                    {
                        chainCalls++;
                        return new CountingTaskScheduler(scheduler, () => chainEnqueueCalls++);
                    })
                .BuildServiceProvider();

            var taskScheduler = container.GetRequiredService<ITaskScheduler>();
            var result = await taskScheduler.Enqueue(() => 5);

            Assert.That(result, Is.EqualTo(5));
            Assert.That(chainCalls, Is.EqualTo(1));
            Assert.That(chainEnqueueCalls, Is.EqualTo(1));
        }

        [Test]
        public void AddTaskFlow_NamedRegistration_ShouldUseNamedCustomFactory()
        {
            var namedFactoryCalls = 0;
            var expectedOptions = new TaskFlowOptions { SynchronousDisposeTimeout = TimeSpan.FromSeconds(9) };

            using var container = new ServiceCollection()
                .AddTaskFlow()
                .AddTaskFlow(
                    name: "named",
                    baseTaskFlowFactory: (_, options) =>
                    {
                        namedFactoryCalls++;
                        return new StubTaskFlow(options);
                    },
                    configureOptions: _ => expectedOptions)
                .BuildServiceProvider();

            var taskFlowFactory = container.GetRequiredService<ITaskFlowFactory>();
            using var namedFlow = taskFlowFactory.CreateTaskFlow("named");

            Assert.That(namedFactoryCalls, Is.EqualTo(1));
            Assert.That(namedFlow.Options, Is.SameAs(expectedOptions));
        }

        [Test]
        public void AddTaskFlow_WhenDuplicateNamedOptionsRegistered_ShouldThrowOnCreateTaskFlow()
        {
            using var container = new ServiceCollection()
                .AddTaskFlow("dup", new TaskFlowOptions { SynchronousDisposeTimeout = TimeSpan.FromSeconds(1) })
                .AddTaskFlow("dup", new TaskFlowOptions { SynchronousDisposeTimeout = TimeSpan.FromSeconds(2) })
                .BuildServiceProvider();

            var taskFlowFactory = container.GetRequiredService<ITaskFlowFactory>();

            Assert.That(() => taskFlowFactory.CreateTaskFlow("dup"), Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void AddTaskFlow_WhenDuplicateNamedFactoriesRegistered_ShouldThrowOnCreateTaskFlow()
        {
            using var container = new ServiceCollection()
                .AddTaskFlow("dup", baseTaskFlowFactory: (_, options) => new StubTaskFlow(options))
                .AddTaskFlow("dup", baseTaskFlowFactory: (_, options) => new StubTaskFlow(options))
                .BuildServiceProvider();

            var taskFlowFactory = container.GetRequiredService<ITaskFlowFactory>();

            Assert.That(() => taskFlowFactory.CreateTaskFlow("dup"), Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void AddTaskFlow_WhenDuplicateNamedChainsRegistered_ShouldThrowOnCreateTaskFlow()
        {
            using var container = new ServiceCollection()
                .AddTaskFlow("dup", configureSchedulerChain: (scheduler, _) => scheduler)
                .AddTaskFlow("dup", configureSchedulerChain: (scheduler, _) => scheduler)
                .BuildServiceProvider();

            var taskFlowFactory = container.GetRequiredService<ITaskFlowFactory>();

            Assert.That(() => taskFlowFactory.CreateTaskFlow("dup"), Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void AddTaskFlow_WithSchedulerChain_DisposingScopeShouldDisposeUnderlyingTaskFlow()
        {
            TrackingTaskFlow? createdTaskFlow = null;

            using var container = new ServiceCollection()
                .AddTaskFlow(
                    name: null,
                    baseTaskFlowFactory: (_, options) => createdTaskFlow = new TrackingTaskFlow(options),
                    configureSchedulerChain: (scheduler, _) => scheduler)
                .BuildServiceProvider();

            using var scope = container.GetRequiredService<IServiceScopeFactory>().CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<ITaskScheduler>();

            Assert.That(createdTaskFlow, Is.Not.Null);
            Assert.That(createdTaskFlow!.DisposeCount, Is.EqualTo(0));

            scope.Dispose();

            Assert.That(createdTaskFlow.DisposeCount, Is.GreaterThanOrEqualTo(1));
        }

        private sealed class CountingTaskScheduler : ITaskScheduler
        {
            private readonly ITaskScheduler _inner;
            private readonly Action _onEnqueue;

            public CountingTaskScheduler(ITaskScheduler inner, Action onEnqueue)
            {
                _inner = inner;
                _onEnqueue = onEnqueue;
            }

            public Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
            {
                _onEnqueue();
                return _inner.Enqueue(taskFunc, state, cancellationToken);
            }
        }

        private sealed class StubTaskFlow : ITaskFlow
        {
            public StubTaskFlow(TaskFlowOptions options)
            {
                Options = options;
            }

            public TaskFlowOptions Options { get; }

            public Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
            {
                return taskFunc(state, cancellationToken).AsTask();
            }

            public ValueTask DisposeAsync()
            {
                return default;
            }

            public void Dispose()
            {
            }

            public bool Dispose(TimeSpan timeout)
            {
                return true;
            }
        }

        private sealed class TrackingTaskFlow : ITaskFlow
        {
            public TrackingTaskFlow(TaskFlowOptions options)
            {
                Options = options;
            }

            public int DisposeCount { get; private set; }

            public TaskFlowOptions Options { get; }

            public Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
            {
                return taskFunc(state, cancellationToken).AsTask();
            }

            public ValueTask DisposeAsync()
            {
                DisposeCount++;
                return default;
            }

            public void Dispose()
            {
                DisposeCount++;
            }

            public bool Dispose(TimeSpan timeout)
            {
                DisposeCount++;
                return true;
            }
        }
    }
}