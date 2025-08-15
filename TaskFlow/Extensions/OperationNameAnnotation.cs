namespace System.Threading.Tasks.Flow
{
    /// <summary>
    /// Provides an operation annotation that stores a name identifier for task scheduler operations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="OperationNameAnnotation"/> class implements <see cref="IOperationAnnotation"/> 
    /// to provide a simple way to name and identify operations within the TaskFlow system. This annotation
    /// is commonly used for logging, debugging, error reporting, and monitoring purposes.
    /// </para>
    /// <para>
    /// The operation name annotation is typically created through the 
    /// <see cref="AnnotatingTaskSchedulerExtensions.WithOperationName(ITaskScheduler, string)"/> 
    /// extension method, which automatically wraps the scheduler with this annotation.
    /// </para>
    /// <para>
    /// Key characteristics:
    /// </para>
    /// <list type="bullet">
    ///   <item>Immutable - the operation name cannot be changed after creation</item>
    ///   <item>Lightweight - stores only the operation name string</item>
    ///   <item>Thread-safe - can be safely accessed from multiple threads</item>
    ///   <item>Integrated - automatically used by other TaskFlow extension methods</item>
    /// </list>
    /// <para>
    /// This annotation is automatically recognized by several TaskFlow extension methods:
    /// </para>
    /// <list type="bullet">
    ///   <item>Error handling extensions include the operation name in error context</item>
    ///   <item>Timeout extensions include the operation name in timeout exception messages</item>
    ///   <item>Logging extensions can extract the operation name for structured logging</item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <para>Basic usage with operation naming:</para>
    /// <code>
    /// ITaskScheduler scheduler = // ... obtain scheduler
    /// 
    /// // Create named scheduler - this creates an OperationNameAnnotation internally
    /// var namedScheduler = scheduler.WithOperationName("ProcessPayment");
    /// 
    /// // The operation name is available to error handlers
    /// var result = await namedScheduler
    ///     .OnError&lt;InvalidOperationException&gt;((sched, ex, nameAnnotation) => 
    ///         Console.WriteLine($"Error in operation '{nameAnnotation?.OperationName}': {ex.Message}"))
    ///     .Enqueue(() => ProcessPaymentAsync());
    /// </code>
    /// <para>Accessing the annotation directly:</para>
    /// <code>
    /// await namedScheduler.AnnotatedEnqueue&lt;string, OperationNameAnnotation&gt;(
    ///     (state, nameAnnotation, token) => {
    ///         var operationName = nameAnnotation?.OperationName ?? "Unknown";
    ///         Console.WriteLine($"Starting operation: {operationName}");
    ///         
    ///         // Perform the actual work
    ///         return ValueTask.FromResult("completed");
    ///     },
    ///     state: null,
    ///     CancellationToken.None);
    /// </code>
    /// <para>Integration with timeout handling:</para>
    /// <code>
    /// var result = await scheduler
    ///     .WithOperationName("DatabaseQuery")
    ///     .WithTimeout(TimeSpan.FromSeconds(30))
    ///     .Enqueue(() => QueryDatabaseAsync());
    /// 
    /// // If timeout occurs, the exception message will include "Operation `DatabaseQuery` has timed out in 00:00:30"
    /// </code>
    /// </example>
    public sealed class OperationNameAnnotation : IOperationAnnotation
    {
        internal OperationNameAnnotation(string operationName)
        {
            OperationName = operationName;
        }

        /// <summary>
        /// Gets the name associated with the operation.
        /// </summary>
        /// <value>A string representing the operation name that was specified when the annotation was created.</value>
        /// <remarks>
        /// <para>
        /// The operation name is immutable and cannot be changed after the annotation is created.
        /// This ensures consistent identification of operations throughout their execution lifecycle.
        /// </para>
        /// <para>
        /// The operation name is used by various TaskFlow extension methods for:
        /// </para>
        /// <list type="bullet">
        ///   <item>Including operation context in error messages and exceptions</item>
        ///   <item>Providing operation identification in timeout scenarios</item>
        ///   <item>Supporting structured logging and monitoring</item>
        ///   <item>Enabling operation-specific debugging and diagnostics</item>
        /// </list>
        /// </remarks>
        public string OperationName { get; }
    }
}