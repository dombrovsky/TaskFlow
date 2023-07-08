namespace TaskFlow.Extensions.Microsoft.DependencyInjection.Tests
{
    using global::Microsoft.Extensions.DependencyInjection;
    using NUnit.Framework;
    using System.Threading.Tasks.Flow;

    [TestFixture]
    public sealed class RealTaskFlowServiceCollectionExtensionsFixture
    {
        [Test]
        public void AddTaskFlow_ShouldRegisterTaskFlowInfoAndTaskScheduler()
        {
            using var container = new ServiceCollection()
                .AddTaskFlow()
                .BuildServiceProvider();

            var taskFlowInfo = container.GetRequiredService<ITaskFlowInfo>();
            var taskScheduler = container.GetRequiredService<ITaskScheduler>();

            Assert.That(taskFlowInfo, Is.SameAs(taskScheduler));
        }

        [Test]
        public void AddTaskFlow_ShouldNotRegisterTaskFlow()
        {
            using var container = new ServiceCollection()
                .AddTaskFlow()
                .BuildServiceProvider();

            var taskFlow = container.GetService<ITaskFlow>();

            Assert.That(taskFlow, Is.Null);
        }

        [Test]
        public void AddTaskFlow_ScopesShouldHaveOwnTaskFlow()
        {
            using var container = new ServiceCollection()
                .AddTaskFlow()
                .BuildServiceProvider();

            var scopeFactory = container.GetRequiredService<IServiceScopeFactory>();
            using var scope1 = scopeFactory.CreateScope();
            using var scope2 = scopeFactory.CreateScope();

            var taskScheduler1 = scope1.ServiceProvider.GetRequiredService<ITaskScheduler>();
            var taskScheduler2 = scope2.ServiceProvider.GetRequiredService<ITaskScheduler>();

            Assert.That(taskScheduler1, Is.Not.SameAs(taskScheduler2));
        }

        [Test]
        public void AddTaskFlow_Scopes_WhenDisposeOneScopeCanEnqueueToAnotherScope()
        {
            using var container = new ServiceCollection()
                .AddTaskFlow()
                .BuildServiceProvider();

            var scopeFactory = container.GetRequiredService<IServiceScopeFactory>();
            using var scope1 = scopeFactory.CreateScope();
            using var scope2 = scopeFactory.CreateScope();

            var taskScheduler1 = scope1.ServiceProvider.GetRequiredService<ITaskScheduler>();
            var taskScheduler2 = scope2.ServiceProvider.GetRequiredService<ITaskScheduler>();

            scope1.Dispose();

            Assert.That(() => taskScheduler1.Enqueue(() => { }), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => taskScheduler2.Enqueue(() => { }), Throws.Nothing);
        }

        [Test]
        [Timeout(1000)]
        public void AddTaskFlow_DisposeScope_ShouldCancelPendingTask()
        {
            using var container = new ServiceCollection()
                .AddTaskFlow()
                .BuildServiceProvider();
            var scope = container.GetRequiredService<IServiceScopeFactory>().CreateScope();
            var taskScheduler = scope.ServiceProvider.GetRequiredService<ITaskScheduler>();

            var task1 = taskScheduler.Enqueue(token => Task.Delay(1000, token)).AsTask();
            var task2 = taskScheduler.Enqueue(token => Task.Delay(1000, token)).AsTask();
            var task3 = taskScheduler.Enqueue(token => Task.Delay(1000, token)).AsTask();
            Assert.That(task1.IsCompleted && task2.IsCompleted && task3.IsCompleted, Is.False);

            scope.Dispose();
            Assert.That(() => task1.IsCanceled, Is.True.After(100, 10), task1.Status.ToString);
            Assert.That(() => task2.IsCanceled, Is.True.After(100, 10), task2.Status.ToString);
            Assert.That(() => task3.IsCanceled, Is.True.After(100, 10), task3.Status.ToString);
        }

        [Test]
        [Timeout(1000)]
        public async Task AddTaskFlow_DisposeScopeAsync_ShouldCancelPendingTask()
        {
            using var container = new ServiceCollection()
                .AddTaskFlow()
                .BuildServiceProvider();
            var scope = container.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
            var taskScheduler = scope.ServiceProvider.GetRequiredService<ITaskScheduler>();

            var task1 = taskScheduler.Enqueue(token => Task.Delay(1000, token)).AsTask();
            var task2 = taskScheduler.Enqueue(token => Task.Delay(1000, token)).AsTask();
            var task3 = taskScheduler.Enqueue(token => Task.Delay(1000, token)).AsTask();
            Assert.That(task1.IsCompleted && task2.IsCompleted && task3.IsCompleted, Is.False);

            await scope.DisposeAsync();
            Assert.That(() => task1.IsCanceled, Is.True.After(100, 10), task1.Status.ToString);
            Assert.That(() => task2.IsCanceled, Is.True.After(100, 10), task2.Status.ToString);
            Assert.That(() => task3.IsCanceled, Is.True.After(100, 10), task3.Status.ToString);
        }

        [Test]
        public void AddTaskFlow_WithOptions_ShouldRegisterTaskFlowWithOptions()
        {
            var options = new TaskFlowOptions { SynchronousDisposeTimeout = TimeSpan.FromDays(1) };

            using var container = new ServiceCollection()
                .AddTaskFlow(options)
                .BuildServiceProvider();

            var taskFlowInfo = container.GetRequiredService<ITaskFlowInfo>();

            Assert.That(taskFlowInfo.Options, Is.EqualTo(options));
        }
    }
}
