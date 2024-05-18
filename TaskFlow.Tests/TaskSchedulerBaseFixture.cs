namespace TaskFlow.Tests
{
    using NUnit.Framework;
    using System.Threading.Tasks;
    using System.Threading.Tasks.Flow;

    [TestFixture]
    [FixtureTimeout(5000)]
    public abstract partial class TaskSchedulerBaseFixture<T>
        where T : ITaskScheduler
    {
        private T _sut;

        [SetUp]
        public void Setup()
        {
            _sut = CreateSut();
        }

        [TearDown]
        public async Task TearDown()
        {
            switch (_sut)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }

        protected abstract T CreateSut();
    }
};