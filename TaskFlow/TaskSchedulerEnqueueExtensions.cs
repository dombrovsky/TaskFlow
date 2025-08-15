namespace System.Threading.Tasks.Flow
{
    using System.ComponentModel;
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks.Flow.Annotations;
    using System.Threading.Tasks.Flow.Internal;

    /// <summary>
    /// Provides extension methods for <see cref="ITaskScheduler"/> to simplify task enqueueing operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class provides a comprehensive set of extension methods that allow scheduling various types of operations
    /// on an <see cref="ITaskScheduler"/> without having to manually adapt function signatures and handle state passing.
    /// The methods handle conversion between different task types (<see cref="Task"/>, <see cref="ValueTask"/>, 
    /// synchronous functions, and actions) and provide convenient overloads for common scenarios.
    /// </para>
    /// <para>
    /// All extension methods perform argument validation and ensure proper exception propagation.
    /// Methods that don't specify a <see cref="CancellationToken"/> will use <see cref="CancellationToken.None"/>.
    /// </para>
    /// <para>
    /// The extension methods support:
    /// </para>
    /// <list type="bullet">
    ///   <item>Functions returning <see cref="Task{TResult}"/> and <see cref="ValueTask{TResult}"/></item>
    ///   <item>Functions returning <see cref="Task"/> and <see cref="ValueTask"/></item>
    ///   <item>Synchronous functions and actions</item>
    ///   <item>Functions with and without state parameters</item>
    ///   <item>Operations with and without cancellation token support</item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// ITaskScheduler scheduler = // ... obtain scheduler
    /// 
    /// // Enqueue a function that returns a value
    /// var result = await scheduler.Enqueue(() => 42);
    /// 
    /// // Enqueue an async operation with cancellation
    /// var asyncResult = await scheduler.Enqueue(async token => {
    ///     await SomeAsyncOperation(token);
    ///     return "completed";
    /// }, cancellationToken);
    /// 
    /// // Enqueue an action
    /// await scheduler.Enqueue(() => Console.WriteLine("Hello World"));
    /// </code>
    /// </example>
    public static class TaskSchedulerEnqueueExtensions
    {
        /// <summary>
        /// Enqueues a function that returns a <see cref="ValueTask{TResult}"/> for execution.
        /// </summary>
        /// <typeparam name="T">The type of result produced by the function.</typeparam>
        /// <param name="taskScheduler">The task scheduler to enqueue the operation on.</param>
        /// <param name="taskFunc">The function to execute that accepts a cancellation token and returns a <see cref="ValueTask{TResult}"/>.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the result of the enqueued function.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the scheduler has been disposed.</exception>
        /// <remarks>
        /// This method adapts a function that takes only a cancellation token to the scheduler's signature
        /// that requires a state parameter by passing <c>null</c> as the state.
        /// </remarks>
        public static Task<T> Enqueue<T>(this ITaskScheduler taskScheduler, Func<CancellationToken, ValueTask<T>> taskFunc, CancellationToken cancellationToken)
        {
            Argument.NotNull(taskScheduler);

            return taskScheduler.Enqueue(TaskFunc, null, cancellationToken);

            ValueTask<T> TaskFunc(object? state, CancellationToken token)
            {
                return taskFunc(token);
            }
        }

        /// <summary>
        /// Enqueues a function that returns a <see cref="Task{TResult}"/> for execution.
        /// </summary>
        /// <typeparam name="T">The type of result produced by the function.</typeparam>
        /// <param name="taskScheduler">The task scheduler to enqueue the operation on.</param>
        /// <param name="taskFunc">The function to execute that accepts a cancellation token and returns a <see cref="Task{TResult}"/>.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the result of the enqueued function.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the scheduler has been disposed.</exception>
        /// <remarks>
        /// This method wraps the <see cref="Task{TResult}"/> returned by the function in a <see cref="ValueTask{TResult}"/>
        /// to match the scheduler's expected signature.
        /// </remarks>
        public static Task<T> Enqueue<T>(this ITaskScheduler taskScheduler, Func<CancellationToken, Task<T>> taskFunc, CancellationToken cancellationToken)
        {
            Argument.NotNull(taskScheduler);

            return taskScheduler.Enqueue(TaskFunc, null, cancellationToken);

            ValueTask<T> TaskFunc(object? state, CancellationToken token)
            {
                return new ValueTask<T>(taskFunc(token));
            }
        }

        /// <summary>
        /// Enqueues a function that returns a <see cref="ValueTask{TResult}"/> for execution with state.
        /// </summary>
        /// <typeparam name="T">The type of result produced by the function.</typeparam>
        /// <typeparam name="TState">The type of state passed to the function.</typeparam>
        /// <param name="taskScheduler">The task scheduler to enqueue the operation on.</param>
        /// <param name="taskFunc">The function to execute that accepts state and a cancellation token and returns a <see cref="ValueTask{TResult}"/>.</param>
        /// <param name="state">The state object to pass to the function.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the result of the enqueued function.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the scheduler has been disposed.</exception>
        /// <remarks>
        /// This method adapts a strongly-typed state parameter function to the scheduler's object-based state signature
        /// by performing the necessary type casting.
        /// </remarks>
        public static Task<T> Enqueue<T, TState>(this ITaskScheduler taskScheduler, Func<TState, CancellationToken, ValueTask<T>> taskFunc, TState state, CancellationToken cancellationToken)
        {
            Argument.NotNull(taskScheduler);

            return taskScheduler.Enqueue(TaskFunc, state, cancellationToken);

            ValueTask<T> TaskFunc(object? s, CancellationToken token)
            {
                return taskFunc((TState)s!, token);
            }
        }

        /// <summary>
        /// Enqueues a function that returns a <see cref="ValueTask"/> for execution with state.
        /// </summary>
        /// <typeparam name="TState">The type of state passed to the function.</typeparam>
        /// <param name="taskScheduler">The task scheduler to enqueue the operation on.</param>
        /// <param name="taskFunc">The function to execute that accepts state and a cancellation token and returns a <see cref="ValueTask"/>.</param>
        /// <param name="state">The state object to pass to the function.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A <see cref="Task"/> representing the completion of the enqueued function.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the scheduler has been disposed.</exception>
        /// <remarks>
        /// This method wraps the non-generic <see cref="ValueTask"/> in a <see cref="ValueTask{TResult}"/> with a 
        /// <see cref="Unit"/> result to match the scheduler's signature requirement.
        /// </remarks>
        public static Task Enqueue<TState>(this ITaskScheduler taskScheduler, Func<TState, CancellationToken, ValueTask> taskFunc, TState state, CancellationToken cancellationToken)
        {
            Argument.NotNull(taskScheduler);

            return taskScheduler.Enqueue(TaskFunc, state, cancellationToken);

            async ValueTask<Unit> TaskFunc(object? s, CancellationToken token)
            {
                await taskFunc((TState)s!, token).ConfigureAwait(false);
                return default;
            }
        }

        /// <summary>
        /// Enqueues a function that returns a <see cref="Task{TResult}"/> for execution with state.
        /// </summary>
        /// <typeparam name="T">The type of result produced by the function.</typeparam>
        /// <typeparam name="TState">The type of state passed to the function.</typeparam>
        /// <param name="taskScheduler">The task scheduler to enqueue the operation on.</param>
        /// <param name="taskFunc">The function to execute that accepts state and a cancellation token and returns a <see cref="Task{TResult}"/>.</param>
        /// <param name="state">The state object to pass to the function.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the result of the enqueued function.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the scheduler has been disposed.</exception>
        /// <remarks>
        /// This method wraps the <see cref="Task{TResult}"/> returned by the function in a <see cref="ValueTask{TResult}"/>
        /// and adapts the strongly-typed state parameter to the scheduler's object-based state signature.
        /// </remarks>
        public static Task<T> Enqueue<T, TState>(this ITaskScheduler taskScheduler, Func<TState, CancellationToken, Task<T>> taskFunc, TState state, CancellationToken cancellationToken)
        {
            Argument.NotNull(taskScheduler);

            return taskScheduler.Enqueue(TaskFunc, state, cancellationToken);

            ValueTask<T> TaskFunc(object? s, CancellationToken token)
            {
                return new ValueTask<T>(taskFunc((TState)s!, token));
            }
        }

        /// <summary>
        /// Enqueues a function that returns a <see cref="Task"/> for execution with state.
        /// </summary>
        /// <typeparam name="TState">The type of state passed to the function.</typeparam>
        /// <param name="taskScheduler">The task scheduler to enqueue the operation on.</param>
        /// <param name="taskFunc">The function to execute that accepts state and a cancellation token and returns a <see cref="Task"/>.</param>
        /// <param name="state">The state object to pass to the function.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A <see cref="Task"/> representing the completion of the enqueued function.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the scheduler has been disposed.</exception>
        /// <remarks>
        /// This method wraps the non-generic <see cref="Task"/> in a <see cref="ValueTask"/> to match the scheduler's
        /// signature and casts the state parameter appropriately.
        /// </remarks>
        public static Task Enqueue<TState>(this ITaskScheduler taskScheduler, Func<TState, CancellationToken, Task> taskFunc, TState state, CancellationToken cancellationToken)
        {
            Argument.NotNull(taskScheduler);

            return taskScheduler.Enqueue(TaskFunc, (object?)state, cancellationToken);

            ValueTask TaskFunc(object? s, CancellationToken token)
            {
                return new ValueTask(taskFunc((TState)s!, token));
            }
        }

        /// <summary>
        /// Enqueues a function that returns a <see cref="Task"/> for execution.
        /// </summary>
        /// <param name="taskScheduler">The task scheduler to enqueue the operation on.</param>
        /// <param name="taskFunc">The function to execute that accepts a cancellation token and returns a <see cref="Task"/>.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A <see cref="Task"/> representing the completion of the enqueued function.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the scheduler has been disposed.</exception>
        /// <remarks>
        /// This method wraps the non-generic <see cref="Task"/> in a <see cref="ValueTask{TResult}"/> with a 
        /// <see cref="Unit"/> result to match the scheduler's signature requirement.
        /// </remarks>
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

        /// <summary>
        /// Enqueues a function that returns a <see cref="Task"/> for execution without cancellation support.
        /// </summary>
        /// <param name="taskScheduler">The task scheduler to enqueue the operation on.</param>
        /// <param name="taskFunc">The function to execute that accepts a cancellation token and returns a <see cref="Task"/>.</param>
        /// <returns>A <see cref="Task"/> representing the completion of the enqueued function.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the scheduler has been disposed.</exception>
        /// <remarks>
        /// This method uses <see cref="CancellationToken.None"/> for the cancellation token.
        /// </remarks>
        public static async Task Enqueue(this ITaskScheduler taskScheduler, Func<CancellationToken, Task> taskFunc)
        {
            Argument.NotNull(taskScheduler);

            await taskScheduler.Enqueue(taskFunc, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Enqueues a function that returns a <see cref="Task{TResult}"/> for execution without cancellation support.
        /// </summary>
        /// <typeparam name="T">The type of result produced by the function.</typeparam>
        /// <param name="taskScheduler">The task scheduler to enqueue the operation on.</param>
        /// <param name="taskFunc">The function to execute that accepts a cancellation token and returns a <see cref="Task{TResult}"/>.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the result of the enqueued function.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the scheduler has been disposed.</exception>
        /// <remarks>
        /// This method uses <see cref="CancellationToken.None"/> for the cancellation token and wraps the 
        /// <see cref="Task{TResult}"/> in a <see cref="ValueTask{TResult}"/>.
        /// </remarks>
        public static Task<T> Enqueue<T>(this ITaskScheduler taskScheduler, Func<CancellationToken, Task<T>> taskFunc)
        {
            Argument.NotNull(taskScheduler);

            return taskScheduler.Enqueue(token => new ValueTask<T>(taskFunc(token)), CancellationToken.None);
        }

        /// <summary>
        /// Enqueues a function that returns a <see cref="Task{TResult}"/> for execution without any parameters.
        /// </summary>
        /// <typeparam name="T">The type of result produced by the function.</typeparam>
        /// <param name="taskScheduler">The task scheduler to enqueue the operation on.</param>
        /// <param name="taskFunc">The function to execute that returns a <see cref="Task{TResult}"/>.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the result of the enqueued function.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the scheduler has been disposed.</exception>
        /// <remarks>
        /// This method uses <see cref="CancellationToken.None"/> for the cancellation token and ignores the
        /// cancellation token parameter when calling the function.
        /// </remarks>
        public static Task<T> Enqueue<T>(this ITaskScheduler taskScheduler, Func<Task<T>> taskFunc)
        {
            Argument.NotNull(taskScheduler);

            return taskScheduler.Enqueue(_ => new ValueTask<T>(taskFunc()), CancellationToken.None);
        }

        /// <summary>
        /// Enqueues a function that returns a <see cref="Task"/> for execution without any parameters.
        /// </summary>
        /// <param name="taskScheduler">The task scheduler to enqueue the operation on.</param>
        /// <param name="taskFunc">The function to execute that returns a <see cref="Task"/>.</param>
        /// <returns>A <see cref="Task"/> representing the completion of the enqueued function.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the scheduler has been disposed.</exception>
        /// <remarks>
        /// This method uses <see cref="CancellationToken.None"/> for the cancellation token and ignores the
        /// cancellation token parameter when calling the function.
        /// </remarks>
        public static Task Enqueue(this ITaskScheduler taskScheduler, Func<Task> taskFunc)
        {
            Argument.NotNull(taskScheduler);

            return taskScheduler.Enqueue(_ => taskFunc(), CancellationToken.None);
        }

        /// <summary>
        /// Enqueues a function that returns a <see cref="ValueTask"/> for execution.
        /// </summary>
        /// <param name="taskScheduler">The task scheduler to enqueue the operation on.</param>
        /// <param name="valueTaskFunc">The function to execute that accepts a cancellation token and returns a <see cref="ValueTask"/>.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <param name="_">Dummy parameter to disambiguate from other overloads. Should always be <c>null</c>.</param>
        /// <returns>A <see cref="Task"/> representing the completion of the enqueued function.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the scheduler has been disposed.</exception>
        /// <remarks>
        /// This method wraps the non-generic <see cref="ValueTask"/> in a <see cref="ValueTask{TResult}"/> with a 
        /// <see cref="Unit"/> result to match the scheduler's signature requirement. The dummy parameter is used
        /// to avoid method signature conflicts with other overloads.
        /// </remarks>
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

        /// <summary>
        /// Enqueues a function that returns a <see cref="ValueTask"/> for execution without cancellation support.
        /// </summary>
        /// <param name="taskScheduler">The task scheduler to enqueue the operation on.</param>
        /// <param name="valueTaskFunc">The function to execute that accepts a cancellation token and returns a <see cref="ValueTask"/>.</param>
        /// <param name="_">Dummy parameter to disambiguate from other overloads. Should always be <c>null</c>.</param>
        /// <returns>A <see cref="Task"/> representing the completion of the enqueued function.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the scheduler has been disposed.</exception>
        /// <remarks>
        /// This method uses <see cref="CancellationToken.None"/> for the cancellation token. The dummy parameter 
        /// is used to avoid method signature conflicts with other overloads.
        /// </remarks>
        public static async Task Enqueue(this ITaskScheduler taskScheduler, Func<CancellationToken, ValueTask> valueTaskFunc, DummyParameter? _ = null)
        {
            Argument.NotNull(taskScheduler);

            await taskScheduler.Enqueue(valueTaskFunc, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Enqueues a function that returns a <see cref="ValueTask{TResult}"/> for execution without cancellation support.
        /// </summary>
        /// <typeparam name="T">The type of result produced by the function.</typeparam>
        /// <param name="taskScheduler">The task scheduler to enqueue the operation on.</param>
        /// <param name="valueTaskFunc">The function to execute that accepts a cancellation token and returns a <see cref="ValueTask{TResult}"/>.</param>
        /// <param name="_">Dummy parameter to disambiguate from other overloads. Should always be <c>null</c>.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the result of the enqueued function.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the scheduler has been disposed.</exception>
        /// <remarks>
        /// This method uses <see cref="CancellationToken.None"/> for the cancellation token. The dummy parameter 
        /// is used to avoid method signature conflicts with other overloads.
        /// </remarks>
        public static Task<T> Enqueue<T>(this ITaskScheduler taskScheduler, Func<CancellationToken, ValueTask<T>> valueTaskFunc, DummyParameter? _ = null)
        {
            Argument.NotNull(taskScheduler);

            return taskScheduler.Enqueue(valueTaskFunc, CancellationToken.None);
        }

        /// <summary>
        /// Enqueues a function that returns a <see cref="ValueTask{TResult}"/> for execution without any parameters.
        /// </summary>
        /// <typeparam name="T">The type of result produced by the function.</typeparam>
        /// <param name="taskScheduler">The task scheduler to enqueue the operation on.</param>
        /// <param name="valueTaskFunc">The function to execute that returns a <see cref="ValueTask{TResult}"/>.</param>
        /// <param name="_">Dummy parameter to disambiguate from other overloads. Should always be <c>null</c>.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the result of the enqueued function.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the scheduler has been disposed.</exception>
        /// <remarks>
        /// This method uses <see cref="CancellationToken.None"/> for the cancellation token and ignores the
        /// cancellation token parameter when calling the function. The dummy parameter is used to avoid method
        /// signature conflicts with other overloads.
        /// </remarks>
        public static Task<T> Enqueue<T>(this ITaskScheduler taskScheduler, Func<ValueTask<T>> valueTaskFunc, DummyParameter? _ = null)
        {
            Argument.NotNull(taskScheduler);

            return taskScheduler.Enqueue(_ => valueTaskFunc(), CancellationToken.None);
        }

        /// <summary>
        /// Enqueues a function that returns a <see cref="ValueTask"/> for execution without any parameters.
        /// </summary>
        /// <param name="taskScheduler">The task scheduler to enqueue the operation on.</param>
        /// <param name="valueTaskFunc">The function to execute that returns a <see cref="ValueTask"/>.</param>
        /// <param name="_">Dummy parameter to disambiguate from other overloads. Should always be <c>null</c>.</param>
        /// <returns>A <see cref="Task"/> representing the completion of the enqueued function.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the scheduler has been disposed.</exception>
        /// <remarks>
        /// This method uses <see cref="CancellationToken.None"/> for the cancellation token and ignores the
        /// cancellation token parameter when calling the function. The dummy parameter is used to avoid method
        /// signature conflicts with other overloads.
        /// </remarks>
        public static Task Enqueue(this ITaskScheduler taskScheduler, Func<ValueTask> valueTaskFunc, DummyParameter? _ = null)
        {
            Argument.NotNull(taskScheduler);

            return taskScheduler.Enqueue(_ => valueTaskFunc(), CancellationToken.None, _);
        }

        /// <summary>
        /// Enqueues an action for execution with cancellation support.
        /// </summary>
        /// <param name="taskScheduler">The task scheduler to enqueue the operation on.</param>
        /// <param name="action">The action to execute that accepts a cancellation token.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A <see cref="Task"/> representing the completion of the enqueued action.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the scheduler has been disposed.</exception>
        /// <remarks>
        /// This method wraps the synchronous action in a <see cref="ValueTask"/> to match the scheduler's
        /// asynchronous interface.
        /// </remarks>
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

        /// <summary>
        /// Enqueues an action for execution without cancellation support.
        /// </summary>
        /// <param name="taskScheduler">The task scheduler to enqueue the operation on.</param>
        /// <param name="action">The action to execute that accepts a cancellation token.</param>
        /// <returns>A <see cref="Task"/> representing the completion of the enqueued action.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the scheduler has been disposed.</exception>
        /// <remarks>
        /// This method uses <see cref="CancellationToken.None"/> for the cancellation token and wraps the 
        /// synchronous action in a <see cref="ValueTask"/>.
        /// </remarks>
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

        /// <summary>
        /// Enqueues an action for execution without any parameters.
        /// </summary>
        /// <param name="taskScheduler">The task scheduler to enqueue the operation on.</param>
        /// <param name="action">The action to execute.</param>
        /// <returns>A <see cref="Task"/> representing the completion of the enqueued action.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the scheduler has been disposed.</exception>
        /// <remarks>
        /// This method uses <see cref="CancellationToken.None"/> for the cancellation token, ignores the
        /// cancellation token parameter when calling the action, and wraps the synchronous action in a <see cref="ValueTask"/>.
        /// </remarks>
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

        /// <summary>
        /// Enqueues an action for execution with cancellation support.
        /// </summary>
        /// <param name="taskScheduler">The task scheduler to enqueue the operation on.</param>
        /// <param name="action">The action to execute.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A <see cref="Task"/> representing the completion of the enqueued action.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the scheduler has been disposed.</exception>
        /// <remarks>
        /// This method ignores the cancellation token parameter when calling the action and wraps the 
        /// synchronous action in a <see cref="ValueTask"/>.
        /// </remarks>
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

        /// <summary>
        /// Enqueues a synchronous function for execution.
        /// </summary>
        /// <typeparam name="T">The type of result produced by the function.</typeparam>
        /// <param name="taskScheduler">The task scheduler to enqueue the operation on.</param>
        /// <param name="func">The synchronous function to execute.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the result of the enqueued function.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the scheduler has been disposed.</exception>
        /// <remarks>
        /// This method uses <see cref="CancellationToken.None"/> for the cancellation token, ignores the
        /// cancellation token parameter when calling the function, and wraps the synchronous result in a <see cref="ValueTask{TResult}"/>.
        /// </remarks>
        public static Task<T> Enqueue<T>(this ITaskScheduler taskScheduler, Func<T> func)
        {
            Argument.NotNull(taskScheduler);

            return taskScheduler.Enqueue(_ => new ValueTask<T>(func()), CancellationToken.None);
        }

        /// <summary>
        /// Enqueues a synchronous function for execution with cancellation support.
        /// </summary>
        /// <typeparam name="T">The type of result produced by the function.</typeparam>
        /// <param name="taskScheduler">The task scheduler to enqueue the operation on.</param>
        /// <param name="func">The synchronous function to execute that accepts a cancellation token.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the result of the enqueued function.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the scheduler has been disposed.</exception>
        /// <remarks>
        /// This method wraps the synchronous result in a <see cref="ValueTask{TResult}"/> to match the scheduler's
        /// asynchronous interface.
        /// </remarks>
        public static Task<T> Enqueue<T>(this ITaskScheduler taskScheduler, Func<CancellationToken, T> func, CancellationToken cancellationToken)
        {
            Argument.NotNull(taskScheduler);

            return taskScheduler.Enqueue(token => new ValueTask<T>(func(token)), cancellationToken);
        }

        /// <summary>
        /// A class with no possible value other than null. Used to mark an optional parameter which should never be set.
        /// Taken from <see href="https://github.com/dotnet/csharplang/discussions/4360#discussioncomment-312520"/>.
        /// </summary>
        /// <remarks>
        /// This type is used as a dummy parameter in method overloads to disambiguate between methods that would
        /// otherwise have identical signatures. It ensures that the parameter can only be <c>null</c> and should
        /// never be explicitly set by callers.
        /// </remarks>
        [EditorBrowsable(EditorBrowsableState.Never)]
        [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "Not intended to use")]
        public sealed class DummyParameter
        {
            private DummyParameter() { }
        }
    }
}