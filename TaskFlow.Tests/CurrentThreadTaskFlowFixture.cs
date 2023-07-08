namespace TaskFlow.Tests
{
    using NUnit.Framework;
    using System.Threading.Tasks.Flow;

    [TestFixture]
    public sealed class CurrentThreadTaskFlowFixture : TaskFlowBaseFixture<CurrentThreadTaskFlow>
    {
        private readonly List<CurrentThreadTaskFlow> _suts = new();

        [SetUp]
        public void CurrentThreadTaskFlowFixtureInitialize()
        {
            _suts.Clear();
        }

        [TearDown]
        public async Task DisposeCurrentThreadTaskFlow()
        {
            await Task.WhenAll(_suts.Select(flow => flow.DisposeAsync().AsTask()));
        }

        protected override CurrentThreadTaskFlow CreateSut()
        {
            var taskFlow = CreateSutNotStarted();
            new Thread(taskFlow.Run) { IsBackground = true }.Start();
            return taskFlow;
        }

        private CurrentThreadTaskFlow CreateSutNotStarted()
        {
            var taskFlow = new CurrentThreadTaskFlow();
            _suts.Add(taskFlow);
            return taskFlow;
        }
    }
}