namespace System.Threading.Tasks.Flow
{
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks;

    /// <summary>Represents the remaining completion pipeline supplied to completion middleware.</summary>
    /// <typeparam name="TResult">The type of result carried by a successful outcome.</typeparam>
    /// <param name="context">The current operation context to pass to the next completion phase.</param>
    /// <param name="outcome">The success or failure to expose to later completion middleware.</param>
    /// <returns>A value task containing the final outcome produced by the remaining completion middleware.</returns>
    /// <remarks>
    /// Invoke this delegate once to preserve the normal completion chain. Passing the received outcome unchanged preserves captured exception identity
    /// and stack information. Completion processing is claimed once per scheduled operation; calling this delegate does not enqueue or execute the user
    /// operation again.
    /// </remarks>
    [SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "The public API deliberately identifies continuation delegate types.")]
    public delegate ValueTask<TaskSchedulerOperationOutcome<TResult>> TaskSchedulerCompletionDelegate<TResult>(
        TaskSchedulerOperationContext context,
        TaskSchedulerOperationOutcome<TResult> outcome);
}
