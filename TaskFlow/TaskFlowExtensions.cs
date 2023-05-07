namespace System.Threading.Tasks.Flow
{
    using System.Threading.Tasks.Flow.Annotations;

    public static class TaskFlowExtensions
    {
        public static async ValueTask Enqueue(this ITaskScheduler taskScheduler, Func<CancellationToken, Task> taskFunc, CancellationToken cancellationToken)
        {
            Argument.NotNull(taskScheduler);

            await taskScheduler.Enqueue(TaskFunc, cancellationToken).ConfigureAwait(false);

            async ValueTask<Unit> TaskFunc(CancellationToken token)
            {
                await taskFunc(token).ConfigureAwait(false);
                return default;
            }
        }

        public static async ValueTask Enqueue(this ITaskScheduler taskScheduler, Func<CancellationToken, Task> taskFunc)
        {
            Argument.NotNull(taskScheduler);

            await taskScheduler.Enqueue(taskFunc, CancellationToken.None).ConfigureAwait(false);
        }

        public static ValueTask<T> Enqueue<T>(this ITaskScheduler taskScheduler, Func<CancellationToken, Task<T>> taskFunc)
        {
            Argument.NotNull(taskScheduler);

            return taskScheduler.Enqueue(token => new ValueTask<T>(taskFunc(token)), CancellationToken.None);
        }

        public static ValueTask<T> Enqueue<T>(this ITaskScheduler taskScheduler, Func<Task<T>> taskFunc)
        {
            Argument.NotNull(taskScheduler);

            return taskScheduler.Enqueue(_ => new ValueTask<T>(taskFunc()), CancellationToken.None);
        }

        public static ValueTask Enqueue(this ITaskScheduler taskScheduler, Func<Task> taskFunc)
        {
            Argument.NotNull(taskScheduler);

            return taskScheduler.Enqueue(_ => taskFunc(), CancellationToken.None);
        }

        public static async ValueTask Enqueue(this ITaskScheduler taskScheduler, Func<CancellationToken, ValueTask> taskFunc, CancellationToken cancellationToken)
        {
            Argument.NotNull(taskScheduler);

            await taskScheduler.Enqueue(TaskFunc, cancellationToken).ConfigureAwait(false);

            async ValueTask<Unit> TaskFunc(CancellationToken token)
            {
                await taskFunc(token).ConfigureAwait(false);
                return default;
            }
        }

        public static async ValueTask Enqueue(this ITaskScheduler taskScheduler, Func<CancellationToken, ValueTask> taskFunc)
        {
            Argument.NotNull(taskScheduler);

            await taskScheduler.Enqueue(taskFunc, CancellationToken.None).ConfigureAwait(false);
        }

        public static ValueTask<T> Enqueue<T>(this ITaskScheduler taskScheduler, Func<CancellationToken, ValueTask<T>> taskFunc)
        {
            Argument.NotNull(taskScheduler);

            return taskScheduler.Enqueue(taskFunc, CancellationToken.None);
        }

        public static ValueTask<T> Enqueue<T>(this ITaskScheduler taskScheduler, Func<ValueTask<T>> taskFunc)
        {
            Argument.NotNull(taskScheduler);

            return taskScheduler.Enqueue(_ => taskFunc(), CancellationToken.None);
        }

        public static ValueTask Enqueue(this ITaskScheduler taskScheduler, Func<ValueTask> taskFunc)
        {
            Argument.NotNull(taskScheduler);

            return taskScheduler.Enqueue(_ => taskFunc(), CancellationToken.None);
        }

        public static async ValueTask Enqueue(this ITaskScheduler taskScheduler, Action<CancellationToken> action, CancellationToken cancellationToken)
        {
            Argument.NotNull(taskScheduler);

            await taskScheduler.Enqueue(
                    token =>
                    {
                        action(token);
                        return new ValueTask();
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        public static async ValueTask Enqueue(this ITaskScheduler taskScheduler, Action<CancellationToken> action)
        {
            Argument.NotNull(taskScheduler);

            await taskScheduler.Enqueue(
                    token =>
                    {
                        action(token);
                        return new ValueTask();
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        public static async ValueTask Enqueue(this ITaskScheduler taskScheduler, Action action)
        {
            Argument.NotNull(taskScheduler);

            await taskScheduler.Enqueue(
                    _ =>
                    {
                        action();
                        return new ValueTask();
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        public static async ValueTask Enqueue(this ITaskScheduler taskScheduler, Action action, CancellationToken cancellationToken)
        {
            Argument.NotNull(taskScheduler);

            await taskScheduler.Enqueue(
                    _ =>
                    {
                        action();
                        return new ValueTask();
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        public static ValueTask<T> Enqueue<T>(this ITaskScheduler taskScheduler, Func<T> func)
        {
            Argument.NotNull(taskScheduler);

            return taskScheduler.Enqueue(_ => new ValueTask<T>(func()), CancellationToken.None);
        }

        public static ValueTask<T> Enqueue<T>(this ITaskScheduler taskScheduler, Func<CancellationToken, T> func, CancellationToken cancellationToken)
        {
            Argument.NotNull(taskScheduler);

            return taskScheduler.Enqueue(token => new ValueTask<T>(func(token)), cancellationToken);
        }
    }
}