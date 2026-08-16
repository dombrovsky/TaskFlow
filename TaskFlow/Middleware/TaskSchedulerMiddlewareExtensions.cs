namespace System.Threading.Tasks.Flow
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Threading.Tasks.Flow.Annotations;
    using System.Threading.Tasks.Flow.Internal;

    /// <summary>Provides immutable middleware and annotation composition for <see cref="ITaskScheduler"/>.</summary>
    /// <remarks>
    /// <para>Each method returns a new non-owning scheduler snapshot. The source scheduler remains unchanged and may be reused to create independent
    /// branches.</para>
    /// <para>Pipeline snapshots do not dispose or otherwise assume ownership of terminal schedulers, middleware instances, or their collaborators.</para>
    /// </remarks>
    public static class TaskSchedulerMiddlewareExtensions
    {
        /// <summary>Creates a scheduler snapshot containing every phase implemented by one middleware object.</summary>
        /// <param name="taskScheduler">The scheduler or pipeline snapshot to extend.</param>
        /// <param name="middleware">The middleware object implementing one or more phase interfaces.</param>
        /// <returns>A new immutable, non-owning scheduler snapshot. The supplied scheduler is not modified.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="taskScheduler"/> or <paramref name="middleware"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="middleware"/> implements no phase interface.</exception>
        /// <remarks>
        /// <para>A single-phase object registers that phase. When one object implements multiple phase interfaces, all phases are registered atomically
        /// at one pipeline position, capture the same forward annotation scope, and share one local-state slot per operation.</para>
        /// <para>Enqueue middleware runs newest registration first. Execution and completion middleware run in registration order. Registration does not
        /// invoke or take ownership of the middleware.</para>
        /// <para>If <paramref name="taskScheduler"/> is not already a TaskFlow pipeline snapshot, it is treated as an opaque terminal boundary; wrappers
        /// are not inspected or flattened.</para>
        /// </remarks>
        /// <example>
        /// <code>
        /// ITaskScheduler scheduler = terminal
        ///     .UseMiddleware(new AdmissionMiddleware())
        ///     .UseMiddleware(new TelemetryMiddleware());
        /// </code>
        /// </example>
        public static ITaskScheduler UseMiddleware(this ITaskScheduler taskScheduler, ITaskSchedulerMiddleware middleware)
        {
            Argument.NotNull(taskScheduler);
            Argument.NotNull(middleware);
            if (!(middleware is ITaskSchedulerEnqueueMiddleware) &&
                !(middleware is ITaskSchedulerExecutionMiddleware) &&
                !(middleware is ITaskSchedulerCompletionMiddleware))
            {
                throw new ArgumentException("Middleware must implement at least one task scheduler middleware phase.", nameof(middleware));
            }

            return GetPipeline(taskScheduler).Append(middleware);
        }

        /// <summary>Creates a scheduler snapshot with an additional forward-scoped annotation.</summary>
        /// <typeparam name="TAnnotation">The annotation type used as the lookup key.</typeparam>
        /// <param name="taskScheduler">The scheduler or pipeline snapshot to extend.</param>
        /// <param name="annotation">The annotation visible to later registrations and the final operation.</param>
        /// <returns>A new immutable scheduler snapshot with the extended annotation scope. The supplied scheduler is not modified.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="taskScheduler"/> or <paramref name="annotation"/> is <c>null</c>.</exception>
        /// <remarks>
        /// <para>The annotation is visible to middleware registered after this call and to the final annotation-aware operation. Middleware already
        /// registered on <paramref name="taskScheduler"/> retains its earlier scope.</para>
        /// <para>Lookup uses <typeparamref name="TAnnotation"/> as the exact key. A later annotation registered with the same key shadows this value for
        /// later registrations without mutating earlier snapshots or sibling branches.</para>
        /// <para>The returned pipeline does not dispose or otherwise own <paramref name="annotation"/>.</para>
        /// </remarks>
        /// <example>
        /// <code>
        /// ITaskScheduler scheduler = terminal
        ///     .WithAnnotation&lt;TenantAnnotation&gt;(new TenantAnnotation("north"))
        ///     .UseMiddleware(new TenantTelemetryMiddleware());
        /// </code>
        /// </example>
        public static ITaskScheduler WithAnnotation<TAnnotation>(this ITaskScheduler taskScheduler, TAnnotation annotation)
            where TAnnotation : class, IOperationAnnotation
        {
            Argument.NotNull(taskScheduler);
            Argument.NotNull(annotation);
            return GetPipeline(taskScheduler).WithAnnotation(typeof(TAnnotation), annotation);
        }

        /// <summary>Enqueues an operation that receives the final pipeline operation context.</summary>
        /// <typeparam name="TResult">The operation result type.</typeparam>
        /// <param name="taskScheduler">The scheduler used to enqueue the operation.</param>
        /// <param name="taskFunc">The context-aware operation delegate.</param>
        /// <param name="state">Optional caller state exposed through the operation context.</param>
        /// <param name="cancellationToken">The submitting caller's cancellation token.</param>
        /// <returns>A task representing the scheduled operation.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="taskScheduler"/> or <paramref name="taskFunc"/> is <c>null</c>.</exception>
        /// <exception cref="ObjectDisposedException">The terminal scheduler has been disposed.</exception>
        /// <remarks>On a non-pipeline scheduler the context contains the supplied state and token but no annotations.</remarks>
        internal static Task<TResult> EnqueueWithContext<TResult>(
            this ITaskScheduler taskScheduler,
            TaskSchedulerExecutionDelegate<TResult> taskFunc,
            object? state,
            CancellationToken cancellationToken)
        {
            Argument.NotNull(taskScheduler);
            Argument.NotNull(taskFunc);

            if (taskScheduler is PipelineTaskScheduler pipeline)
            {
                var scheduler = pipeline.UseMiddleware(new ContextOperationMiddleware<TResult>(taskFunc));
                return scheduler.Enqueue<TResult>(
                    (Func<object?, CancellationToken, ValueTask<TResult>>)((_, __) => throw new InvalidOperationException("The context operation middleware did not execute.")),
                    state,
                    cancellationToken);
            }

            return taskScheduler.Enqueue(async (s, token) =>
            {
                var operation = new PipelineOperation<TResult>((_, __) => throw new InvalidOperationException(), s, 0, null, cancellationToken);
                var context = new TaskSchedulerOperationContext(operation, null, -1, token);
                return await taskFunc(context).ConfigureAwait(true);
            }, state, cancellationToken);
        }

        private static PipelineTaskScheduler GetPipeline(ITaskScheduler taskScheduler)
            => taskScheduler as PipelineTaskScheduler ?? new PipelineTaskScheduler(taskScheduler);

        private sealed class ContextOperationMiddleware<TResult> : ITaskSchedulerExecutionMiddleware
        {
            private readonly TaskSchedulerExecutionDelegate<TResult> _taskFunc;
            public ContextOperationMiddleware(TaskSchedulerExecutionDelegate<TResult> taskFunc) => _taskFunc = taskFunc;

            public async ValueTask<T> InvokeAsync<T>(TaskSchedulerOperationContext context, TaskSchedulerExecutionDelegate<T> next)
            {
                if (typeof(T) != typeof(TResult)) return await next(context).ConfigureAwait(true);
                var result = await _taskFunc(context).ConfigureAwait(true);
                return (T)(object?)result!;
            }
        }

    }
}
