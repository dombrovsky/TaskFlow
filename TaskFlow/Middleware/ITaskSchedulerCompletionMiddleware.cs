namespace System.Threading.Tasks.Flow
{
    using System.Threading.Tasks;

    /// <summary>Defines middleware that observes, handles, or replaces an operation's success or failure outcome.</summary>
    /// <remarks>
    /// <para>Completion middleware is invoked in registration order after execution, or after an enqueue or terminal submission failure.</para>
    /// <para>Execution failures run completion on the terminal context. Failures that occur before the terminal invokes the operation cannot guarantee
    /// terminal thread or synchronization-context affinity.</para>
    /// </remarks>
    public interface ITaskSchedulerCompletionMiddleware : ITaskSchedulerMiddleware
    {
        /// <summary>Processes the current outcome and continues completion processing.</summary>
        /// <typeparam name="TResult">The operation result type.</typeparam>
        /// <param name="context">Immutable operation facts, effective cancellation, captured annotations, and this registration's local state.</param>
        /// <param name="outcome">The success or failure produced by execution or preceding completion middleware.</param>
        /// <param name="continuation">The remaining completion pipeline.</param>
        /// <returns>A value task containing the outcome that should ultimately complete the scheduled task.</returns>
        /// <remarks>
        /// <para>Pass <paramref name="outcome"/> unchanged to preserve the identity and captured dispatch information of an existing exception. Pass an
        /// outcome created by <see cref="TaskSchedulerOperationOutcome{TResult}.FromResult"/> or
        /// <see cref="TaskSchedulerOperationOutcome{TResult}.FromException"/> to intentionally replace it.</para>
        /// <para>Middleware should normally invoke <paramref name="continuation"/> exactly once. If this method throws before invoking it, the thrown
        /// exception replaces the current outcome and later completion middleware still runs. If it throws after invoking it, later middleware is not
        /// repeated and the thrown exception becomes the final failure.</para>
        /// </remarks>
        ValueTask<TaskSchedulerOperationOutcome<TResult>> InvokeAsync<TResult>(
            TaskSchedulerOperationContext context,
            TaskSchedulerOperationOutcome<TResult> outcome,
            TaskSchedulerCompletionDelegate<TResult> continuation);
    }
}
