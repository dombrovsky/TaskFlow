namespace System.Threading.Tasks.Flow
{
    public interface IDefaultTaskFlowFactory
    {
        ITaskFlow Create(TaskFlowOptions options);
    }
}