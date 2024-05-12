namespace System.Threading.Tasks.Flow.Internal
{
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

        public static async Task<TResult> WhenAnyCancelRest<TResult>(this IEnumerable<Func<CancellationToken, Task<TResult>>> taskFactories, CancellationToken cancellationToken)
        {
            Argument.NotNull(taskFactories);

            using var cts = new CancellationTokenSource();
            using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);

            try
            {
                var tasks = taskFactories.Select(func => func(linkedCancellationTokenSource.Token)).ToArray();
                var firstFinishedTask = await Task.WhenAny(tasks).ConfigureAwait(false);

                foreach (var task in tasks.Where(task => task != firstFinishedTask))
                {
                    HandleException(task);
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

            static void HandleException(Task<TResult> task)
            {
                task.ContinueWith(t => t.Exception!,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
    }
}