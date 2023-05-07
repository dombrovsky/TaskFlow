namespace System.Threading.Tasks.Flow
{
    public interface ITaskScheduler
    {
        ValueTask<T> Enqueue<T>(Func<CancellationToken, ValueTask<T>> taskFunc, CancellationToken cancellationToken);
    }
}