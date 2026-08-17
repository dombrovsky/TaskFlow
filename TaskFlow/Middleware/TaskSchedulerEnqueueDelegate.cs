namespace System.Threading.Tasks.Flow
{
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks;

    /// <summary>Represents the remaining enqueue pipeline supplied to enqueue middleware.</summary>
    /// <typeparam name="TResult">The type of result produced by the scheduled operation.</typeparam>
    /// <param name="context">The immutable enqueue context to pass onward. Its producer cancellation token is forwarded to later enqueue middleware
    /// and eventually to the terminal scheduler.</param>
    /// <returns>A task representing the remaining admission steps, terminal scheduling, and operation completion.</returns>
    /// <remarks>
    /// Invoke this delegate at most once for a given middleware invocation. Enqueue middleware may omit the call to short-circuit scheduling. The
    /// delegate accepts only contexts belonging to the current operation; use <see cref="TaskSchedulerEnqueueContext{TResult}.WithCancellationToken"/>
    /// to change the effective producer token without losing operation identity or metadata.
    /// </remarks>
    [SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "The public API deliberately identifies continuation delegate types.")]
    public delegate Task<TResult> TaskSchedulerEnqueueDelegate<TResult>(TaskSchedulerEnqueueContext<TResult> context);
}
