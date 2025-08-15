namespace System.Threading.Tasks.Flow
{
    using System.Linq;
    using System.Threading.Tasks.Flow.Annotations;

    /// <summary>
    /// Provides extension methods for <see cref="ITaskScheduler"/> to add operation annotation capabilities.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class enables annotating task scheduler operations with metadata that can be used for 
    /// monitoring, debugging, error handling, and other cross-cutting concerns. Annotations are 
    /// attached to operations through extended state and can be retrieved by other extension methods
    /// in the TaskFlow pipeline.
    /// </para>
    /// <para>
    /// The annotation system works by:
    /// </para>
    /// <list type="bullet">
    ///   <item>Storing annotation data in extended state objects</item>
    ///   <item>Providing methods to enqueue operations with annotation access</item>
    ///   <item>Enabling retrieval of annotations by type in downstream operations</item>
    /// </list>
    /// <para>
    /// Common use cases include:
    /// </para>
    /// <list type="bullet">
    ///   <item>Adding operation names for logging and debugging</item>
    ///   <item>Storing correlation IDs for distributed tracing</item>
    ///   <item>Attaching user context for authorization</item>
    ///   <item>Providing metadata for monitoring and metrics</item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <para>Basic operation naming:</para>
    /// <code>
    /// ITaskScheduler scheduler = // ... obtain scheduler
    /// 
    /// // Add operation name annotation
    /// var namedScheduler = scheduler.WithOperationName("ProcessUserData");
    /// 
    /// // Enqueue operation with annotation access
    /// await namedScheduler.AnnotatedEnqueue&lt;string, OperationNameAnnotation&gt;(
    ///     (state, annotation, token) => {
    ///         Console.WriteLine($"Executing: {annotation?.OperationName}")
    ///         return ValueTask.FromResult("completed");
    ///     },
    ///     state: null,
    ///     CancellationToken.None);
    /// </code>
    /// <para>Custom annotation integration:</para>
    /// <code>
    /// public class UserContextAnnotation : IOperationAnnotation
    /// {
    ///     public string UserId { get; set; }
    ///     public string Role { get; set; }
    /// }
    /// 
    /// var contextScheduler = scheduler.WithExtendedState(new UserContextAnnotation 
    /// { 
    ///     UserId = "user123", 
    ///     Role = "admin" 
    /// });
    /// 
    /// await contextScheduler.AnnotatedEnqueue&lt;void, UserContextAnnotation&gt;(
    ///     (state, context, token) => {
    ///         if (context?.Role == "admin") {
    ///             // Execute admin operation
    ///         }
    ///         return ValueTask.CompletedTask;
    ///     },
    ///     state: null,
    ///     CancellationToken.None);
    /// </code>
    /// </example>
    public static class AnnotatingTaskSchedulerExtensions
    {
        /// <summary>
        /// Creates a task scheduler wrapper that attaches an operation name annotation to all enqueued tasks.
        /// </summary>
        /// <param name="taskScheduler">The task scheduler to wrap with operation name annotation.</param>
        /// <param name="operationName">The name to associate with operations executed on this scheduler.</param>
        /// <returns>An <see cref="ITaskScheduler"/> that includes the operation name annotation in its extended state.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="operationName"/> is <c>null</c>, empty, or whitespace.</exception>
        /// <remarks>
        /// <para>
        /// This method creates a wrapper scheduler that automatically attaches an <see cref="OperationNameAnnotation"/>
        /// to all operations. The operation name can be retrieved by other extension methods in the TaskFlow pipeline,
        /// such as error handlers, timeout handlers, and logging components.
        /// </para>
        /// <para>
        /// The annotation is stored in the extended state and does not affect the core operation execution.
        /// Multiple annotations can be layered by chaining extension methods.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// ITaskScheduler scheduler = // ... obtain scheduler
        /// 
        /// var namedScheduler = scheduler.WithOperationName("DataProcessing");
        /// 
        /// // The operation name will be available to error handlers, timeout handlers, etc.
        /// var result = await namedScheduler
        ///     .WithTimeout(TimeSpan.FromSeconds(30))
        ///     .OnError&lt;TimeoutException&gt;((sched, ex, name) => 
        ///         Console.WriteLine($"Operation '{name?.OperationName}' timed out"))
        ///     .Enqueue(() => ProcessDataAsync());
        /// </code>
        /// </example>
        public static ITaskScheduler WithOperationName(this ITaskScheduler taskScheduler, string operationName)
        {
            Argument.NotEmpty(operationName);

            return taskScheduler.WithExtendedState(new OperationNameAnnotation( operationName));
        }

        /// <summary>
        /// Enqueues a task function that can access annotation data attached to the scheduler.
        /// </summary>
        /// <typeparam name="T">The type of result produced by the task function.</typeparam>
        /// <typeparam name="TAnnotation">The type of annotation to retrieve and pass to the task function.</typeparam>
        /// <param name="taskScheduler">The task scheduler to enqueue the operation on.</param>
        /// <param name="taskFunc">The function to execute that accepts state, annotation, and cancellation token parameters.</param>
        /// <param name="state">An optional state object that is passed to the <paramref name="taskFunc"/>.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the result of the enqueued task function.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> or <paramref name="taskFunc"/> is <c>null</c>.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the scheduler has been disposed.</exception>
        /// <remarks>
        /// <para>
        /// This method provides a convenient way to enqueue operations that need access to annotation metadata.
        /// The method automatically extracts the first annotation of type <typeparamref name="TAnnotation"/> 
        /// from the scheduler's extended state and passes it to the task function.
        /// </para>
        /// <para>
        /// The annotation parameter will be <c>null</c> if:
        /// </para>
        /// <list type="bullet">
        ///   <item>No annotation of the specified type is found in the extended state</item>
        ///   <item>The scheduler has no extended state</item>
        ///   <item>The annotation type doesn't match any stored annotations</item>
        /// </list>
        /// <para>
        /// Annotation resolution works by unwrapping nested extended state objects and looking for the
        /// first match of the specified annotation type. This allows for complex annotation hierarchies
        /// created by chaining multiple extension methods.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // Setup scheduler with multiple annotations
        /// var scheduler = taskFlow
        ///     .WithOperationName("UserOperation")
        ///     .WithExtendedState(new CustomAnnotation { Priority = "High" });
        /// 
        /// // Access operation name annotation
        /// await scheduler.AnnotatedEnqueue&lt;string, OperationNameAnnotation&gt;(
        ///     (state, nameAnnotation, token) => {
        ///         var operationName = nameAnnotation?.OperationName ?? "Unknown";
        ///         Console.WriteLine($"Executing: {operationName}");
        ///         return ValueTask.FromResult("completed");
        ///     },
        ///     state: null,
        ///     CancellationToken.None);
        /// 
        /// // Access custom annotation
        /// await scheduler.AnnotatedEnqueue&lt;void, CustomAnnotation&gt;(
        ///     (state, customAnnotation, token) => {
        ///         var priority = customAnnotation?.Priority ?? "Normal";
        ///         Console.WriteLine($"Priority: {priority}");
        ///         return ValueTask.CompletedTask;
        ///     },
        ///     state: null,
        ///     CancellationToken.None);
        /// </code>
        /// </example>
        public static Task<T> AnnotatedEnqueue<T, TAnnotation>(this ITaskScheduler taskScheduler, Func<object?, TAnnotation?, CancellationToken, ValueTask<T>> taskFunc, object? state, CancellationToken cancellationToken)
            where TAnnotation : class, IOperationAnnotation
        {
            Argument.NotNull(taskScheduler);
            Argument.NotNull(taskFunc);

            var annotation = (state as ExtendedState)
                .Unwrap<TAnnotation>()
                .FirstOrDefault();

            return taskScheduler.Enqueue((s, c) => taskFunc(s, annotation, c), state, cancellationToken);
        }
    }
}