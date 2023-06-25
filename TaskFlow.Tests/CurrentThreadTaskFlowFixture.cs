namespace TaskFlow.Tests
{
    using NUnit.Framework;
    using System.Threading.Tasks.Flow;

    [TestFixture]
    public sealed class CurrentThreadTaskFlowFixture : TaskFlowBaseFixture<CurrentThreadTaskFlow>
    {
        protected override CurrentThreadTaskFlow CreateSut()
        {
            var taskFlow = new CurrentThreadTaskFlow();
            new Thread(taskFlow.Run).Start();
            return taskFlow;
        }
    }
}