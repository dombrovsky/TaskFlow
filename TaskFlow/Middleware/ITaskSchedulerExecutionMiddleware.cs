namespace System.Threading.Tasks.Flow;

using System.Threading.Tasks;

/// <summary>Defines middleware that surrounds the user operation inside the delegate invoked by the terminal scheduler.</summary>
/// <remarks>
/// <para>Execution middleware is invoked in registration order and runs on the execution context established by the terminal scheduler.</para>
/// <para>Use this phase for timing, tracing, resource scopes, retries, or other behavior that must execute with the scheduled operation.</para>
/// </remarks>
public interface ITaskSchedulerExecutionMiddleware : ITaskSchedulerMiddleware
{
    /// <summary>Invokes, replaces, or repeatedly orchestrates the remaining execution pipeline.</summary>
    /// <typeparam name="TResult">The operation result type.</typeparam>
    /// <param name="context">Immutable operation facts, effective cancellation, captured annotations, and this registration's local state.</param>
    /// <param name="continuation">The next execution middleware or the user operation.</param>
    /// <returns>A value task containing the result selected by this middleware.</returns>
    /// <remarks>
    /// <para>Ordinary middleware should invoke <paramref name="continuation"/> once. Orchestration middleware may invoke it multiple times—for
    /// example, to retry—without enqueueing another terminal delegate. Returning without invoking it replaces the user operation.</para>
    /// <para>Use <see cref="TaskSchedulerOperationContext.WithCancellationToken"/> to forward a replacement effective token to downstream
    /// execution. The continuation requires a context for the same operation; another operation's context is rejected.</para>
    /// <para>A thrown exception or a faulted value task becomes a failed <see cref="TaskSchedulerOperationOutcome{TResult}"/> and is passed to
    /// completion middleware. Use <c>ConfigureAwait(true)</c> when an asynchronous continuation must retain terminal affinity.</para>
    /// <para>Return or await every continuation started by this invocation before its value task settles, including on failure or
    /// cancellation. Concurrent attempts must all be joined. Continuation invocation itself may throw synchronously, so protect
    /// invocation and awaiting together. Detached attempts and invoking a saved continuation after completion are unsupported.</para>
    /// </remarks>
    ValueTask<TResult> InvokeAsync<TResult>(TaskSchedulerOperationContext context, TaskSchedulerExecutionDelegate<TResult> continuation);
}