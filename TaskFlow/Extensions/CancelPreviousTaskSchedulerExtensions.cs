namespace System.Threading.Tasks.Flow
{
    using System.Threading.Tasks.Flow.Annotations;
    using System.Threading.Tasks.Flow.Internal;

    /// <summary>
    /// Provides extension methods for <see cref="ITaskScheduler"/> to implement cancel-previous behavior where each new operation cancels all previously enqueued operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class implements a cancellation pattern where enqueueing a new operation automatically cancels
    /// all previously enqueued operations that haven't completed yet. This is useful for scenarios where
    /// only the most recent operation should be executed, and older operations become obsolete.
    /// </para>
    /// <para>
    /// Key characteristics of cancel-previous behavior:
    /// </para>
    /// <list type="bullet">
    ///   <item>Each new operation cancels all pending operations</item>
    ///   <item>Only the most recently enqueued operation will typically complete</item>
    ///   <item>Already scheduled or running operations are cancelled but may complete if they don't check cancellation tokens promptly</item>
    /// </list>
    /// <para>
    /// Common use cases include:
    /// </para>
    /// <list type="bullet">
    ///   <item>Search suggestions - cancel previous search when user types new characters</item>
    ///   <item>Auto-save operations - cancel previous save when new save is triggered</item>
    ///   <item>Real-time data updates - cancel previous fetch when new data is requested</item>
    ///   <item>UI debouncing - cancel previous UI update when new update is needed</item>
    ///   <item>Progressive loading - cancel previous load operation when new load is started</item>
    /// </list>
    /// <para>
    /// The cancellation mechanism works by maintaining a collection of cancellation tokens and
    /// cancelling all of them when a new operation is enqueued. This ensures that older operations
    /// receive cancellation signals as soon as possible.
    /// </para>
    /// </remarks>
    /// <example>
    /// <para>Search suggestion scenario:</para>
    /// <code>
    /// ITaskScheduler scheduler = // ... obtain scheduler
    /// var cancelPreviousScheduler = scheduler.CreateCancelPrevious();
    /// 
    /// // User types "a" - starts search
    /// var search1 = cancelPreviousScheduler.Enqueue(() => SearchAsync("a"));
    /// 
    /// // User types "ab" - cancels previous search and starts new one
    /// var search2 = cancelPreviousScheduler.Enqueue(() => SearchAsync("ab"));
    /// 
    /// // User types "abc" - cancels previous search and starts new one
    /// var search3 = cancelPreviousScheduler.Enqueue(() => SearchAsync("abc"));
    /// 
    /// // Only search3 will typically complete; search1 and search2 will be cancelled
    /// var results = await search3;
    /// </code>
    /// <para>Auto-save with cancellation:</para>
    /// <code>
    /// var autoSaveScheduler = documentScheduler.CreateCancelPrevious();
    /// 
    /// void OnDocumentChanged()
    /// {
    ///     // Cancel any pending save and start a new one
    ///     _ = autoSaveScheduler.Enqueue(async token => {
    ///         await Task.Delay(1000, token); // Debounce for 1 second
    ///         token.ThrowIfCancellationRequested();
    ///         await SaveDocumentAsync(token);
    ///     });
    /// }
    /// 
    /// // Multiple rapid changes will cancel previous saves
    /// OnDocumentChanged(); // Save 1 started
    /// OnDocumentChanged(); // Save 1 cancelled, Save 2 started  
    /// OnDocumentChanged(); // Save 2 cancelled, Save 3 started
    /// // Only Save 3 will complete
    /// </code>
    /// <para>Real-time data updates:</para>
    /// <code>
    /// var dataUpdateScheduler = scheduler.CreateCancelPrevious();
    /// 
    /// async Task RefreshDataAsync()
    /// {
    ///     try 
    ///     {
    ///         var data = await dataUpdateScheduler.Enqueue(async token => {
    ///             // This will be cancelled if another refresh is triggered
    ///             var freshData = await FetchLatestDataAsync(token);
    ///             token.ThrowIfCancellationRequested();
    ///             return freshData;
    ///         });
    ///         
    ///         UpdateUI(data);
    ///     }
    ///     catch (OperationCanceledException)
    ///     {
    ///         // Previous operation was cancelled - this is expected
    ///     }
    /// }
    /// </code>
    /// </example>
    public static class CancelPreviousTaskSchedulerExtensions
    {
        /// <summary>
        /// Creates a task scheduler wrapper that automatically cancels all previously enqueued operations when a new operation is enqueued.
        /// </summary>
        /// <param name="taskScheduler">The base task scheduler to wrap with cancel-previous functionality.</param>
        /// <returns>An <see cref="ITaskScheduler"/> that implements cancel-previous behavior.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <remarks>
        /// <para>
        /// This method creates a wrapper around the base task scheduler that maintains a collection of
        /// cancellation tokens for all active operations. When a new operation is enqueued:
        /// </para>
        /// <list type="number">
        ///   <item>All existing cancellation tokens are triggered to cancel pending operations</item>
        ///   <item>A new cancellation token is allocated for the new operation</item>
        ///   <item>The new operation is linked with both its original cancellation token and the allocated token</item>
        /// </list>
        /// <para>
        /// The cancellation behavior is cooperative, meaning that operations must check their cancellation
        /// tokens regularly to respond to cancellation requests. Operations that don't check cancellation
        /// tokens may continue running even after being "cancelled".
        /// </para>
        /// <para>
        /// Resource management is handled automatically - cancellation tokens are properly disposed
        /// when operations complete, and the internal cancellation token allocator manages its resources.
        /// </para>
        /// <para>
        /// The wrapper maintains all characteristics of the base scheduler (execution order, concurrency
        /// behavior, etc.) while adding the cancel-previous functionality.
        /// </para>
        /// <para>
        /// Performance considerations:
        /// </para>
        /// <list type="bullet">
        ///   <item>Each enqueue operation triggers cancellation of previous operations</item>
        ///   <item>Cancellation is typically very fast but involves some overhead</item>
        ///   <item>Memory usage is proportional to the number of concurrent operations</item>
        ///   <item>Cancelled operations may still consume resources until they check cancellation tokens</item>
        /// </list>
        /// </remarks>
        /// <example>
        /// <code>
        /// ITaskScheduler baseScheduler = new TaskFlow();
        /// var cancelPreviousScheduler = baseScheduler.CreateCancelPrevious();
        /// 
        /// // Enqueue multiple operations rapidly
        /// var task1 = cancelPreviousScheduler.Enqueue(async token => {
        ///     await Task.Delay(1000, token); // Will likely be cancelled
        ///     return "Task 1";
        /// });
        /// 
        /// var task2 = cancelPreviousScheduler.Enqueue(async token => {
        ///     await Task.Delay(1000, token); // Will likely be cancelled  
        ///     return "Task 2";
        /// });
        /// 
        /// var task3 = cancelPreviousScheduler.Enqueue(async token => {
        ///     await Task.Delay(1000, token); // Most likely to complete
        ///     return "Task 3";
        /// });
        /// 
        /// try 
        /// {
        ///     var result1 = await task1; // Will likely throw OperationCanceledException
        /// }
        /// catch (OperationCanceledException) 
        /// {
        ///     // Expected - task1 was cancelled by task2
        /// }
        /// 
        /// try 
        /// {
        ///     var result2 = await task2; // Will likely throw OperationCanceledException
        /// }
        /// catch (OperationCanceledException) 
        /// {
        ///     // Expected - task2 was cancelled by task3
        /// }
        /// 
        /// var result3 = await task3; // Should complete successfully with "Task 3"
        /// </code>
        /// </example>
        public static ITaskScheduler CreateCancelPrevious(this ITaskScheduler taskScheduler)
        {
            return new CancelPreviousTaskSchedulerWrapper(taskScheduler);
        }

        private sealed class CancelPreviousTaskSchedulerWrapper : ITaskScheduler
        {
            private readonly ITaskScheduler _baseTaskScheduler;
            private readonly CancelAllTokensAllocator _cancelAllTokensAllocator;

            public CancelPreviousTaskSchedulerWrapper(ITaskScheduler baseTaskScheduler)
            {
                Argument.NotNull(baseTaskScheduler);

                _baseTaskScheduler = baseTaskScheduler;
                _cancelAllTokensAllocator = new CancelAllTokensAllocator();
            }

            public async Task<T> Enqueue<T>(Func<object?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
            {
                _cancelAllTokensAllocator.Cancel();

                using (_cancelAllTokensAllocator.AllocateCancellationToken(out var allocatedToken))
                {
                    using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, allocatedToken);
                    return await _baseTaskScheduler.Enqueue(taskFunc, state, linkedToken.Token).ConfigureAwait(false);
                }
            }
        }
    }
}
