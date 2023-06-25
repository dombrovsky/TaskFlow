namespace System.Threading.Tasks.Flow
{
    public interface INamedConfigureTaskFlowChain : IHaveName
    {
        ITaskScheduler ConfigureChain(ITaskScheduler taskScheduler);
    }
}