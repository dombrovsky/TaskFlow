namespace TaskFlow.Extensions.Microsoft.DependencyInjection.Tests
{
    using global::Microsoft.Extensions.DependencyInjection;
    using NUnit.Framework;
    using System.Threading.Tasks.Flow;

    [TestFixture]
    public sealed class ServiceCollectionExtensionsFixture
    {
        [Test]
        public void AddTaskFlow_ShouldRegisterTaskFlowAndTaskScheduler()
        {
            using var container = new ServiceCollection()
                .AddTaskFlow()
                .BuildServiceProvider();

            var taskFlow = container.GetRequiredService<ITaskFlow>();
            var taskScheduler = container.GetRequiredService<ITaskScheduler>();

            Assert.That(taskFlow, Is.SameAs(taskScheduler));
        }

        [Test]
        public void AddTaskFlow_WithOptions_ShouldRegisterTaskFlowWithOptions()
        {
            var options = new TaskFlowOptions { SynchronousDisposeTimeout = TimeSpan.FromDays(1) };

            using var container = new ServiceCollection()
                .AddTaskFlow(options)
                .BuildServiceProvider();

            var taskFlow = container.GetRequiredService<ITaskFlow>();

            Assert.That(taskFlow.Options, Is.EqualTo(options));
        }
    }
}
