namespace TaskFlow.Tests
{
    using JetBrains.dotMemoryUnit;
    using NUnit.Framework;
    using System.Threading.Tasks.Flow;

    public abstract partial class TaskSchedulerBaseFixture<T>
    {
        [Test]
        [DotMemoryUnit(FailIfRunWithoutSupport = false)]
        public void Enqueue_ResultReferencedFromStack_ResultShouldBeAlive()
        {
            var result = _sut.Enqueue(() => new AllocatedResult()).Result;

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);

            dotMemory.Check(memory =>
            {
                Assert.That(memory.GetObjects(where => where.Type.Is<AllocatedResult>()).ObjectsCount, Is.EqualTo(1));
            });

            GC.KeepAlive(result);
        }

        [Test]
        [DotMemoryUnit(FailIfRunWithoutSupport = false)]
        public void Enqueue_WhenFinished_ShouldNotKeepReferenceToResult()
        {
            _sut.Enqueue(() => new AllocatedResult()).Wait();

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);

            dotMemory.Check(memory =>
            {
                Assert.That(memory.GetObjects(where => where.Type.Is<AllocatedResult>()).ObjectsCount, Is.EqualTo(0));
            });
        }

        [Test]
        [DotMemoryUnit(FailIfRunWithoutSupport = false, CollectAllocations = true)]
        public void Enqueue_MultipleTimes_ShouldNotKeepReferencesToAllResults()
        {
            var task = Task.CompletedTask;
            for (int i = 0; i < 100; i++)
            {
                task = _sut.Enqueue(() => new AllocatedResult());
            }
            task.Wait();

            var snapshot1 = dotMemory.Check();

            for (int i = 0; i < 10; i++)
            {
                task = _sut.Enqueue(() => new AllocatedResult());
            }

            task.Wait();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);

            dotMemory.Check(memory =>
            {
                foreach (var typeMemoryInfo in memory.GetDifference(snapshot1).GetNewObjects().GroupByType())
                {
                    Console.WriteLine($"{typeMemoryInfo.ObjectsCount} {typeMemoryInfo.SizeInBytes} {typeMemoryInfo.Type.FullName}");
                }

                Assert.That(memory.GetDifference(snapshot1).GetNewObjects(where => where.Type.Is<AllocatedResult>()).ObjectsCount, Is.EqualTo(0));
            });
        }
    }

    internal sealed class AllocatedResult
    {
        private readonly byte[] _data = new byte[100];
    }
}