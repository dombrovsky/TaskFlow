namespace TaskFlow.Tests.Extensions
{
    using System.Threading.Tasks.Flow;

    internal static class TaskFlows
    {
        public static IEnumerable<ITaskFlow> CreateTaskFlows()
        {
            yield return new TaskFlow();
            yield return new DedicatedThreadTaskFlow();

            var currentThreadTaskFlow = new CurrentThreadTaskFlow();
            var thread = new Thread(currentThreadTaskFlow.Run)
            {
                IsBackground = true,
            };
            thread.Start();

            SpinWait.SpinUntil(() => currentThreadTaskFlow.ThreadId != 0, TimeSpan.FromSeconds(1));
            yield return currentThreadTaskFlow;
        }
    }
}