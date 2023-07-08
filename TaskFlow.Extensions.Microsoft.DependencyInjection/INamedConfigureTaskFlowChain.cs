namespace System.Threading.Tasks.Flow
{
    public interface INamedConfigureTaskFlowChain
    {
        string Name { get; }

        ITaskScheduler ConfigureChain(ITaskScheduler taskScheduler);
    }
}