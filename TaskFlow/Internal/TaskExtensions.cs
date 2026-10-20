namespace System.Threading.Tasks.Flow.Internal;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Flow.Annotations;

internal static class TaskExtensions
{
    public static void Await(this Task task)
    {
        Argument.NotNull(task);

        task.GetAwaiter().GetResult();
    }

    public static T Await<T>(this Task<T> task)
    {
        Argument.NotNull(task);

        return task.GetAwaiter().GetResult();
    }

    public static bool Await(this Task task, TimeSpan timeout)
    {
        Argument.NotNull(task);

        if (timeout == Timeout.InfiniteTimeSpan)
        {
            task.Await();
            return true;
        }

        try
        {
            return task.Wait(timeout);
        }
        catch (AggregateException aggregateException)
        {
            if (aggregateException.InnerException == null)
            {
                throw;
            }

            ExceptionDispatchInfo.Capture(aggregateException.InnerException).Throw();
            throw;
        }
    }

    public static Task<TResult> WhenAnyCancelRest<TResult>(this IEnumerable<Func<CancellationToken, Task<TResult>>> taskFactories, CancellationToken cancellationToken)
    {
        Argument.NotNull(taskFactories);
        var (tasks, cts, linkedCancellationTokenSource) = StartTasks(taskFactories, cancellationToken);
        return AwaitFirst(tasks, cts, linkedCancellationTokenSource);
    }

    private static (Task<TResult>[] Tasks, CancellationTokenSource Cancellation, CancellationTokenSource LinkedCancellation) StartTasks<TResult>(
        IEnumerable<Func<CancellationToken, Task<TResult>>> taskFactories, CancellationToken cancellationToken)
    {
        var cts = new CancellationTokenSource();
        CancellationTokenSource? linkedCancellationTokenSource = null;
        var tasks = new List<Task<TResult>>();
        try
        {
            linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);
            foreach (var factory in taskFactories)
                tasks.Add(factory(linkedCancellationTokenSource.Token));
            return (tasks.ToArray(), cts, linkedCancellationTokenSource);
        }
        catch
        {
            foreach (var task in tasks)
                ObserveFailure(task);
            try
            { cts.Cancel(throwOnFirstException: false); }
            finally
            {
                linkedCancellationTokenSource?.Dispose();
                cts.Dispose();
            }
            throw;
        }
    }

    private static async Task<TResult> AwaitFirst<TResult>(Task<TResult>[] tasks, CancellationTokenSource cts, CancellationTokenSource linkedCancellationTokenSource)
    {
        using (cts)
        using (linkedCancellationTokenSource)
        {
            try
            {
                var firstFinishedTask = await Task.WhenAny(tasks).ConfigureAwait(false);
                foreach (var task in tasks.Where(task => task != firstFinishedTask))
                {
                    ObserveFailure(task);
                }
                return await firstFinishedTask.ConfigureAwait(false);
            }
            finally
            {
#if NET8_0_OR_GREATER
                await cts.CancelAsync().ConfigureAwait(false);
#else
                cts.Cancel();
#endif
            }
        }
    }

    private static void ObserveFailure<TResult>(Task<TResult> task)
    {
        _ = task.ContinueWith(t => t.Exception!,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}