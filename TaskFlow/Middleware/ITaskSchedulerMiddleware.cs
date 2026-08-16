namespace System.Threading.Tasks.Flow
{
    /// <summary>Identifies a component that participates in one or more task-scheduler middleware phases.</summary>
    /// <remarks>
    /// <para>This marker is the common registration type accepted by <see cref="TaskSchedulerMiddlewareExtensions.UseMiddleware"/>.</para>
    /// <para>Implement at least one of <see cref="ITaskSchedulerEnqueueMiddleware"/>, <see cref="ITaskSchedulerExecutionMiddleware"/>, or
    /// <see cref="ITaskSchedulerCompletionMiddleware"/>. A component may implement multiple phase interfaces; one registration then captures one
    /// annotation scope and provides one operation-local state slot shared by all implemented phases.</para>
    /// <para>Middleware instances are reused across scheduled operations. Instance-level mutable state must therefore be safe for concurrent use.
    /// Store state belonging to one operation in the context's local-state slot.</para>
    /// </remarks>
    public interface ITaskSchedulerMiddleware
    {
    }
}
