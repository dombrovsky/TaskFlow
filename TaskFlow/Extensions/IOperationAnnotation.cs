namespace System.Threading.Tasks.Flow
{
    /// <summary>
    /// Defines a marker interface for operation annotations that can be attached to task scheduler operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="IOperationAnnotation"/> interface serves as a marker interface for objects that 
    /// provide metadata about task scheduler operations. Annotations implementing this interface can be
    /// attached to task schedulers through extension methods and retrieved by other components in the
    /// TaskFlow pipeline.
    /// </para>
    /// <para>
    /// This interface enables type-safe annotation systems where different components can:
    /// </para>
    /// <list type="bullet">
    ///   <item>Store operation-specific metadata without affecting core execution</item>
    ///   <item>Retrieve relevant annotations by type for processing</item>
    ///   <item>Create custom annotation types for specific use cases</item>
    ///   <item>Support cross-cutting concerns like logging, monitoring, and error handling</item>
    /// </list>
    /// <para>
    /// Common annotation implementations include:
    /// </para>
    /// <list type="bullet">
    ///   <item><see cref="OperationNameAnnotation"/> - for operation naming and identification</item>
    ///   <item>Custom user context annotations - for authorization and personalization</item>
    ///   <item>Correlation ID annotations - for distributed tracing</item>
    ///   <item>Priority annotations - for operation prioritization</item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <para>Creating a custom annotation:</para>
    /// <code>
    /// public class UserContextAnnotation : IOperationAnnotation
    /// {
    ///     public string UserId { get; set; }
    ///     public string Role { get; set; }
    ///     public string TenantId { get; set; }
    /// }
    /// 
    /// public class CorrelationAnnotation : IOperationAnnotation
    /// {
    ///     public string CorrelationId { get; set; }
    ///     public string RequestId { get; set; }
    /// }
    /// </code>
    /// <para>Using annotations with task schedulers:</para>
    /// <code>
    /// // Attach multiple annotations
    /// var scheduler = taskFlow
    ///     .WithOperationName("ProcessOrder")
    ///     .WithExtendedState(new UserContextAnnotation { UserId = "user123", Role = "customer" })
    ///     .WithExtendedState(new CorrelationAnnotation { CorrelationId = Guid.NewGuid().ToString() });
    /// 
    /// // Access annotations in operations
    /// await scheduler.AnnotatedEnqueue&lt;void, UserContextAnnotation&gt;(
    ///     (state, userContext, token) => {
    ///         // Use user context for authorization
    ///         if (userContext?.Role == "admin") {
    ///             // Execute admin-specific logic
    ///         }
    ///         return ValueTask.CompletedTask;
    ///     },
    ///     state: null,
    ///     CancellationToken.None);
    /// </code>
    /// </example>
    public interface IOperationAnnotation
    {
    }
}