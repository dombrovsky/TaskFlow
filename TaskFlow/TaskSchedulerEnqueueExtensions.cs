namespace System.Threading.Tasks.Flow
{
    using System.ComponentModel;
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks.Flow.Annotations;
    using System.Threading.Tasks.Flow.Internal;

    public static class TaskSchedulerEnqueueExtensions
    {
        public static async Task Enqueue(this ITaskScheduler taskScheduler, Func<CancellationToken, Task> taskFunc, CancellationToken cancellationToken)
        {
            Argument.NotNull(taskScheduler);

            await taskScheduler.Enqueue(TaskFunc, cancellationToken).ConfigureAwait(false);

            async ValueTask<Unit> TaskFunc(CancellationToken token)
            {
                await taskFunc(token).ConfigureAwait(false);
                return default;
            }
        }

        public static async Task Enqueue(this ITaskScheduler taskScheduler, Func<CancellationToken, Task> taskFunc)
        {
            Argument.NotNull(taskScheduler);

            await taskScheduler.Enqueue(taskFunc, CancellationToken.None).ConfigureAwait(false);
        }

        public static Task<T> Enqueue<T>(this ITaskScheduler taskScheduler, Func<CancellationToken, Task<T>> taskFunc)
        {
            Argument.NotNull(taskScheduler);

            return taskScheduler.Enqueue(token => new ValueTask<T>(taskFunc(token)), CancellationToken.None);
        }

        public static Task<T> Enqueue<T>(this ITaskScheduler taskScheduler, Func<Task<T>> taskFunc)
        {
            Argument.NotNull(taskScheduler);

            return taskScheduler.Enqueue(_ => new ValueTask<T>(taskFunc()), CancellationToken.None);
        }

        public static Task Enqueue(this ITaskScheduler taskScheduler, Func<Task> taskFunc)
        {
            Argument.NotNull(taskScheduler);

            return taskScheduler.Enqueue(_ => taskFunc(), CancellationToken.None);
        }

        public static async Task Enqueue(this ITaskScheduler taskScheduler, Func<CancellationToken, ValueTask> valueTaskFunc, CancellationToken cancellationToken, DummyParameter? _ = null)
        {
            Argument.NotNull(taskScheduler);

            await taskScheduler.Enqueue(TaskFunc, cancellationToken).ConfigureAwait(false);

            async ValueTask<Unit> TaskFunc(CancellationToken token)
            {
                await valueTaskFunc(token).ConfigureAwait(false);
                return default;
            }
        }

        public static async Task Enqueue(this ITaskScheduler taskScheduler, Func<CancellationToken, ValueTask> valueTaskFunc, DummyParameter? _ = null)
        {
            Argument.NotNull(taskScheduler);

            await taskScheduler.Enqueue(valueTaskFunc, CancellationToken.None).ConfigureAwait(false);
        }

        public static Task<T> Enqueue<T>(this ITaskScheduler taskScheduler, Func<CancellationToken, ValueTask<T>> valueTaskFunc, DummyParameter? _ = null)
        {
            Argument.NotNull(taskScheduler);

            return taskScheduler.Enqueue(valueTaskFunc, CancellationToken.None);
        }

        public static Task<T> Enqueue<T>(this ITaskScheduler taskScheduler, Func<ValueTask<T>> valueTaskFunc, DummyParameter? _ = null)
        {
            Argument.NotNull(taskScheduler);

            return taskScheduler.Enqueue(_ => valueTaskFunc(), CancellationToken.None);
        }

        public static Task Enqueue(this ITaskScheduler taskScheduler, Func<ValueTask> valueTaskFunc, DummyParameter? _ = null)
        {
            Argument.NotNull(taskScheduler);

            return taskScheduler.Enqueue(_ => valueTaskFunc(), CancellationToken.None, _);
        }

        public static async Task Enqueue(this ITaskScheduler taskScheduler, Action<CancellationToken> action, CancellationToken cancellationToken)
        {
            Argument.NotNull(taskScheduler);

            await taskScheduler.Enqueue(
                    token =>
                    {
                        action(token);
                        return new ValueTask(Task.CompletedTask);
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        public static async Task Enqueue(this ITaskScheduler taskScheduler, Action<CancellationToken> action)
        {
            Argument.NotNull(taskScheduler);

            await taskScheduler.Enqueue(
                    token =>
                    {
                        action(token);
                        return new ValueTask(Task.CompletedTask);
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        public static async Task Enqueue(this ITaskScheduler taskScheduler, Action action)
        {
            Argument.NotNull(taskScheduler);

            await taskScheduler.Enqueue(
                    _ =>
                    {
                        action();
                        return new ValueTask(Task.CompletedTask);
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        public static async Task Enqueue(this ITaskScheduler taskScheduler, Action action, CancellationToken cancellationToken)
        {
            Argument.NotNull(taskScheduler);

            await taskScheduler.Enqueue(
                    _ =>
                    {
                        action();
                        return new ValueTask(Task.CompletedTask);
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        public static Task<T> Enqueue<T>(this ITaskScheduler taskScheduler, Func<T> func)
        {
            Argument.NotNull(taskScheduler);

            return taskScheduler.Enqueue(_ => new ValueTask<T>(func()), CancellationToken.None);
        }

        public static Task<T> Enqueue<T>(this ITaskScheduler taskScheduler, Func<CancellationToken, T> func, CancellationToken cancellationToken)
        {
            Argument.NotNull(taskScheduler);

            return taskScheduler.Enqueue(token => new ValueTask<T>(func(token)), cancellationToken);
        }

        /// <summary>
        /// A class with no possible value other than null. Used to mark an optional parameter which should never be set.
        /// Taken from <see href="https://github.com/dotnet/csharplang/discussions/4360#discussioncomment-312520"/>.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "Not intended to use")]
        public sealed class DummyParameter
        {
            private DummyParameter() { }
        }
    }
}