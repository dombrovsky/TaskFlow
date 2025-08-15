namespace System.Threading.Tasks.Flow
{
    using System.Threading.Tasks.Flow.Annotations;

    /// <summary>
    /// Provides extension methods for <see cref="ITaskScheduler"/> to create cancellation scopes that automatically link cancellation tokens.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class enables the creation of cancellation scopes around task schedulers, allowing for automatic
    /// cancellation token linking. When a cancellation scope is created, all operations enqueued on the
    /// resulting scheduler will have their cancellation tokens linked with the scope's cancellation token.
    /// </para>
    /// <para>
    /// Key benefits of cancellation scopes:
    /// </para>
    /// <list type="bullet">
    ///   <item>Automatic cancellation propagation to all operations within the scope</item>
    ///   <item>Hierarchical cancellation - parent scope cancellation affects all child operations</item>
    ///   <item>Resource cleanup - operations can be cancelled when their containing scope is cancelled</item>
    ///   <item>Simplified cancellation management - no need to manually link tokens for each operation</item>
    /// </list>
    /// <para>
    /// This is particularly useful for:
    /// </para>
    /// <list type="bullet">
    ///   <item>Request-scoped cancellation in web applications</item>
    ///   <item>Component lifecycle management</item>
    ///   <item>Batch operation cancellation</item>
    ///   <item>Resource-bound operation groups</item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <para>Basic cancellation scope usage:</para>
    /// <code>
    /// using var cts = new CancellationTokenSource();
    /// ITaskScheduler scheduler = // ... obtain scheduler
    /// 
    /// // Create a cancellation scope
    /// var scopedScheduler = scheduler.CreateCancellationScope(cts.Token);
    /// 
    /// // All operations on scopedScheduler will be cancelled if cts is cancelled
    /// var task1 = scopedScheduler.Enqueue(() => LongRunningOperation1());
    /// var task2 = scopedScheduler.Enqueue(() => LongRunningOperation2());
    /// 
    /// // Cancel all operations in the scope
    /// cts.Cancel();
    /// 
    /// // Both task1 and task2 will be cancelled
    /// </code>
    /// <para>Hierarchical cancellation with multiple scopes:</para>
    /// <code>
    /// using var parentCts = new CancellationTokenSource();
    /// using var childCts = new CancellationTokenSource();
    /// 
    /// ITaskScheduler baseScheduler = // ... obtain scheduler
    /// 
    /// // Create nested cancellation scopes
    /// var parentScope = baseScheduler.CreateCancellationScope(parentCts.Token);
    /// var childScope = parentScope.CreateCancellationScope(childCts.Token);
    /// 
    /// // Operations in child scope are affected by both parent and child cancellation
    /// var childTask = childScope.Enqueue(() => SomeOperation());
    /// 
    /// // Cancelling either parentCts or childCts will cancel childTask
    /// parentCts.Cancel(); // This will cancel childTask
    /// </code>
    /// <para>Request-scoped web application usage:</para>
    /// <code>
    /// public async Task ProcessRequestAsync(HttpContext context)
    /// {
    ///     // Use request cancellation token to create scope
    ///     var requestScopedScheduler = _scheduler.CreateCancellationScope(context.RequestAborted);
    ///     
    ///     // All operations will be automatically cancelled if request is aborted
    ///     await requestScopedScheduler.Enqueue(() => ProcessDataAsync());
    ///     await requestScopedScheduler.Enqueue(() => SaveResultsAsync());
    ///     
    ///     // No need to manually pass context.RequestAborted to each operation
    /// }
    /// </code>
    /// </example>
    public static class CancellationScopeTaskSchedulerExtensions
    {
        /// <summary>
        /// Creates a task scheduler that automatically links a scope cancellation token with all operation cancellation tokens.
        /// </summary>
        /// <param name="taskScheduler">The base task scheduler to wrap with cancellation scope functionality.</param>
        /// <param name="scopeCancellationToken">The cancellation token that defines the scope. When this token is cancelled, all operations enqueued on the returned scheduler will be cancelled.</param>
        /// <returns>An <see cref="ITaskScheduler"/> that automatically links the scope cancellation token with operation cancellation tokens.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <remarks>
        /// <para>
        /// This method creates a wrapper around the base task scheduler that intercepts all 
        /// <see cref="ITaskScheduler.Enqueue{T}(Func{object?, CancellationToken, ValueTask{T}}, object?, CancellationToken)"/>
        /// calls and creates a linked cancellation token combining the operation's cancellation token 
        /// with the scope cancellation token.
        /// </para>
        /// <para>
        /// The linked token behavior follows these rules:
        /// </para>
        /// <list type="bullet">
        ///   <item>If either the scope token or operation token is cancelled, the linked token is cancelled</item>
        ///   <item>The linked token is automatically disposed after the operation completes</item>
        ///   <item>Cancellation can originate from either the scope or the individual operation</item>
        ///   <item>The first cancellation source to trigger will cancel the operation</item>
        /// </list>
        /// <para>
        /// The returned scheduler maintains all characteristics of the base scheduler (execution order,
        /// concurrency behavior, etc.) while adding automatic cancellation scope functionality.
        /// </para>
        /// <para>
        /// Multiple cancellation scopes can be nested by calling this method multiple times,
        /// creating a hierarchy where any parent scope cancellation affects all child operations.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// ITaskScheduler baseScheduler = // ... obtain scheduler
        /// using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        /// 
        /// // Create scope that cancels all operations after 5 minutes
        /// var scopedScheduler = baseScheduler.CreateCancellationScope(cts.Token);
        /// 
        /// // This operation will be cancelled if either:
        /// // 1. The individual operation token is cancelled, OR
        /// // 2. The scope token (cts.Token) is cancelled after 5 minutes
        /// using var operationCts = new CancellationTokenSource();
        /// var result = await scopedScheduler.Enqueue(
        ///     async token => {
        ///         // token is linked: will be cancelled by either operationCts or cts
        ///         await SomeAsyncOperation(token);
        ///         return "completed";
        ///     }, 
        ///     operationCts.Token);
        /// </code>
        /// </example>
        public static ITaskScheduler CreateCancellationScope(this ITaskScheduler taskScheduler, CancellationToken scopeCancellationToken)
        {
            return new CancellationScopeTaskSchedulerWrapper(taskScheduler, scopeCancellationToken);
        }

        private sealed class CancellationScopeTaskSchedulerWrapper : ITaskScheduler
        {
            private readonly ITaskScheduler _baseTaskScheduler;
            private readonly CancellationToken _scopedCancellationToken;

            public CancellationScopeTaskSchedulerWrapper(ITaskScheduler baseTaskScheduler, CancellationToken scopedCancellationToken)
            {
                Argument.NotNull(baseTaskScheduler);

                _baseTaskScheduler = baseTaskScheduler;
                _scopedCancellationToken = scopedCancellationToken;
            }

            public async Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
            {
                using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _scopedCancellationToken);
                return await _baseTaskScheduler.Enqueue(taskFunc, state, linkedToken.Token).ConfigureAwait(false);
            }
        }
    }
}