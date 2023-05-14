namespace TaskFlow.Tests
{
    using NUnit.Framework;
    using System.Threading.Tasks.Flow;

    [TestFixture]
    public sealed class TaskFlowFixture : TaskFlowBaseFixture<TaskFlow>
    {
        protected override TaskFlow CreateSut()
        {
            return new TaskFlow();
        }
    }
}
