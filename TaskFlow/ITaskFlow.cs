namespace System.Threading.Tasks.Flow
{
    public interface ITaskFlow : ITaskScheduler, IAsyncDisposable, IDisposable
    {
        TaskFlowOptions Options { get; }

        bool Dispose(TimeSpan timeout);
    }
}