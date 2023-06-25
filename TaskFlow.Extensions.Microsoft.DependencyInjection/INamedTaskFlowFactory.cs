namespace System.Threading.Tasks.Flow
{
    public interface INamedTaskFlowFactory : IHaveName
    {
        ITaskFlow Create(TaskFlowOptions options);
    }
}