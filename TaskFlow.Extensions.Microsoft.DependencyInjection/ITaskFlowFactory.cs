namespace System.Threading.Tasks.Flow
{
    public interface ITaskFlowFactory
    {
        ITaskFlow CreateTaskFlow(string? name = null);
    }
}