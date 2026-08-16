namespace TaskFlow.Tests
{
    using NUnit.Framework;
    using System.Threading.Tasks.Flow;

    [TestFixture]
    internal sealed class CurrentThreadTaskFlowFixture : TaskFlowBaseFixture<CurrentThreadTaskFlow>
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

        [Test]
        [CancelAfter(1000)]
        public void Enqueue_BeforeRun_ShouldExecuteAfterRunStarts()
        {
            var taskFlow = CreateSutNotStarted();
            var task = taskFlow.Enqueue(() => 42);

            Assert.That(task.Wait(100), Is.False);

            new Thread(taskFlow.Run) { IsBackground = true }.Start();

            Assert.That(task.Wait(500), Is.True);
            Assert.That(task.Result, Is.EqualTo(42));
        }

        private CurrentThreadTaskFlow CreateSutNotStarted()
        {
            var taskFlow = new CurrentThreadTaskFlow();
            _suts.Add(taskFlow);
            return taskFlow;
        }
    }
}
