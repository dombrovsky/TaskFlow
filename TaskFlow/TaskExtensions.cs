namespace System.Threading.Tasks.Flow
{
    using System.Runtime.ExceptionServices;
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
    }
}