namespace System.Threading.Tasks.Flow
{
    public interface ITaskScheduler
    {
        Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken);
    }
}