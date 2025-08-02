namespace TaskFlow.Extensions.Microsoft.DependencyInjection.Tests
{
    using global::Microsoft.Extensions.DependencyInjection;
    using NSubstitute;
    using NUnit.Framework;
    using System.Threading.Tasks.Flow;

    [TestFixture]
    internal sealed class CustomTaskFlowServiceCollectionExtensionsFixture
    {
        [Test]
        public void AddTaskFlow_ShouldRegisterTaskFlowInfoAndTaskScheduler()
        {
            using var container = new ServiceCollection()
                .AddTaskFlow(null, (provider, options) =>
                {
                    var taskFlow = Substitute.For<ITaskFlow>();
                    taskFlow.Options.Returns(options);
                    return taskFlow;
                })
                .BuildServiceProvider();
        }
    }
}