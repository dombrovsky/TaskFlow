namespace TaskFlow.Tests.Extensions
{
    using System.Threading.Tasks.Flow;

    internal static class TaskFlows
    {
        public static IEnumerable<ITaskFlow> CreateTaskFlows()
        {
            yield return new TaskFlow();
            yield return new DedicatedThreadTaskFlow();
        }
    }
}