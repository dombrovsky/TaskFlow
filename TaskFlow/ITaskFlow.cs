namespace System.Threading.Tasks.Flow
{
    public interface ITaskFlow : ITaskScheduler, IAsyncDisposable, IDisposable
    {
        bool Dispose(TimeSpan timeout);
    }
}