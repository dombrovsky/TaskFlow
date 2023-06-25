namespace System.Threading.Tasks.Flow.Internal
{
    internal sealed class DefaultTaskFlowFactory : IDefaultTaskFlowFactory
    {
        public ITaskFlow Create(TaskFlowOptions options)
        {
            return new TaskFlow(options);
        }
    }
}