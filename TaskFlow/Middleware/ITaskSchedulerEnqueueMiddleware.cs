namespace System.Threading.Tasks.Flow;

using System.Threading.Tasks;

/// <summary>Defines admission middleware that runs before an operation reaches the terminal scheduler.</summary>
/// <remarks>
/// <para>Enqueue middleware is invoked in reverse registration order, so the most recently registered enqueue component runs first.</para>
/// <para>Use this phase for admission, rejection, coalescing, throttling, or producer cancellation-token selection. Invoke the continuation at most
/// once. Returning a task without invoking it short-circuits the remaining enqueue pipeline and the terminal scheduler.</para>
/// </remarks>
public interface ITaskSchedulerEnqueueMiddleware : ITaskSchedulerMiddleware
{
    /// <summary>Processes an operation before it is submitted to the terminal scheduler.</summary>
    /// <typeparam name="TResult">The operation result type.</typeparam>
    /// <param name="context">Immutable operation facts, the effective producer token, captured annotations, and this registration's local state.</param>
    /// <param name="continuation">The remaining enqueue pipeline. Pass the original context or a copy returned by
    /// <see cref="TaskSchedulerEnqueueContext{TResult}.WithCancellationToken"/>.</param>
    /// <returns>A task representing admission and, when the continuation is invoked, terminal scheduling and operation completion.</returns>
    /// <remarks>
    /// <para>Return a completed, failed, or canceled task without calling <paramref name="continuation"/> to satisfy or reject the request without
    /// submitting a terminal delegate. Do not call the continuation more than once.</para>
    /// <para>Failures reported through the returned task enter the completion pipeline even though terminal execution has not started.
    /// Exceptions thrown by this method before it returns a task propagate from enqueue without being converted into completion outcomes.
    /// Middleware and wrappers that return their own tasks retain their exception timing and resource ownership. Direct forwarding
    /// exposes the downstream task's original status: a faulted task containing OperationCanceledException remains Faulted when
    /// inspected by middleware. Await propagates the same exception; the outer scheduled task still reports cancellation.</para>
    /// <para>Before the returned task or value task settles, every downstream continuation started by this invocation must have
    /// settled. Returning the continuation directly, awaiting it before additional work, and short-circuiting are supported.
    /// Execution retries or concurrent attempts must await every started attempt, including on failure and cancellation.
    /// Detached execution/completion and invoking a continuation after this invocation finishes are unsupported usage.
    /// This requirement is not exhaustively checked at runtime.</para>
    /// <para>Policies that intentionally separate caller waiting from producer termination must transfer producer ownership
    /// through <see cref="TaskSchedulerEnqueueContext{TResult}.InvokeWithCallerWait"/>. Returning an independent wait task
    /// after directly invoking the ordinary continuation is unsupported.</para>
    /// </remarks>
    Task<TResult> InvokeAsync<TResult>(TaskSchedulerEnqueueContext<TResult> context, TaskSchedulerEnqueueDelegate<TResult> continuation);
}