namespace System.Threading.Tasks.Flow
{
    public interface ITaskScheduler
    {
        Task<T> Enqueue<T>(Func<CancellationToken, ValueTask<T>> taskFunc, CancellationToken cancellationToken);
    }
}