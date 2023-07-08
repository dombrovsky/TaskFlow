namespace System.Threading.Tasks.Flow
{
    public interface INamedConfigureTaskFlowOptions
    {
        string Name { get; }

        TaskFlowOptions Configure();
    }
}