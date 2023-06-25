namespace TaskFlow.Tests
{
    using NUnit.Framework;
    using System.Threading.Tasks.Flow;

    [TestFixture]
    public sealed class DedicatedThreadTaskFlowFixture : TaskFlowBaseFixture<DedicatedThreadTaskFlow>
    {
        protected override DedicatedThreadTaskFlow CreateSut()
        {
            return new DedicatedThreadTaskFlow();
        }
    }
}