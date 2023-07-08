namespace System.Threading.Tasks.Flow
{
    public interface INamedTaskFlowFactory
    {
        string Name { get; }

        ITaskFlow Create(TaskFlowOptions options);
    }
}