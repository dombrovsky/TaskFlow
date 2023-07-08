namespace System.Threading.Tasks.Flow
{
    public interface ITaskFlow : ITaskScheduler, ITaskFlowInfo, IAsyncDisposable, IDisposable
    {
        bool Dispose(TimeSpan timeout);
    }
}