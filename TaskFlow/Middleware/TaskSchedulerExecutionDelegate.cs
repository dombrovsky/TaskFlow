namespace System.Threading.Tasks.Flow
{
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks;

    /// <summary>Represents the remaining execution pipeline supplied to execution middleware.</summary>
    /// <typeparam name="TResult">The type of result produced by the scheduled operation.</typeparam>
    /// <param name="context">The current operation context to pass to the next execution phase.</param>
    /// <returns>A value task containing the result produced by the remaining execution middleware or user operation.</returns>
    /// <remarks>
    /// The delegate runs inside the terminal scheduler's submitted envelope. It may be invoked more than once by orchestration middleware without
    /// submitting additional terminal delegates. Each invocation may execute the user operation again. Exceptions are captured as operation outcomes
    /// and processed by completion middleware.
    /// </remarks>
    [SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "The public API deliberately identifies continuation delegate types.")]
    public delegate ValueTask<TResult> TaskSchedulerExecutionDelegate<TResult>(TaskSchedulerOperationContext context);
}
