namespace System.Threading.Tasks.Flow
{
    public interface INamedConfigureTaskFlowOptions : IHaveName
    {
        TaskFlowOptions Configure();
    }
}