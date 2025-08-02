namespace System.Threading.Tasks.Flow
{
    /// <summary>
    /// Defines a contract for accessing information about a task flow's configuration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="ITaskFlowInfo"/> interface provides a way to access configuration
    /// information about a task flow without exposing the ability to schedule tasks or
    /// manage the task flow's lifecycle.
    /// </para>
    /// <para>
    /// This interface is useful for components that need to read task flow configuration
    /// but should not have the ability to enqueue tasks or dispose the task flow.
    /// </para>
    /// </remarks>
    public interface ITaskFlowInfo
    {
        /// <summary>
        /// Gets the options that configure the behavior of the task flow.
        /// </summary>
        /// <remarks>
        /// The options object contains configuration settings that affect the behavior
        /// of the task flow, such as the task scheduler to use and timeout settings
        /// for synchronous disposal operations.
        /// </remarks>
        TaskFlowOptions Options { get; }
    }
}