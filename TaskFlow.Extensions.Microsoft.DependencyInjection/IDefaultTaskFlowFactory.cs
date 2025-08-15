namespace System.Threading.Tasks.Flow
{
    /// <summary>
    /// Defines a factory contract for creating default <see cref="ITaskFlow"/> instances with specified options.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="IDefaultTaskFlowFactory"/> interface provides a simple factory abstraction for creating
    /// task flow instances with specific configuration options. This interface serves as the fallback factory
    /// when no named factory is available, ensuring that task flows can always be created with at least
    /// default behavior.
    /// </para>
    /// <para>
    /// Key characteristics of the default factory:
    /// </para>
    /// <list type="bullet">
    ///   <item><strong>Option-based creation</strong> - Takes explicit options rather than resolving them from names</item>
    ///   <item><strong>Fallback behavior</strong> - Used when no specific named factory is registered</item>
    ///   <item><strong>Simple interface</strong> - Single method focused on core task flow creation</item>
    ///   <item><strong>Dependency injection ready</strong> - Designed for registration in DI containers</item>
    /// </list>
    /// <para>
    /// The default implementation typically creates standard <see cref="TaskFlow"/> instances, but
    /// custom implementations can provide different task flow types or behaviors while maintaining
    /// compatibility with the dependency injection system.
    /// </para>
    /// <para>
    /// This interface is particularly useful for:
    /// </para>
    /// <list type="bullet">
    ///   <item>Providing consistent default behavior across the application</item>
    ///   <item>Enabling easy replacement of the default task flow implementation</item>
    ///   <item>Supporting unit testing with mock implementations</item>
    ///   <item>Allowing customization of default creation logic</item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <para>Custom default factory implementation:</para>
    /// <code>
    /// public class CustomDefaultTaskFlowFactory : IDefaultTaskFlowFactory
    /// {
    ///     private readonly ILogger&lt;CustomDefaultTaskFlowFactory&gt; _logger;
    ///     
    ///     public CustomDefaultTaskFlowFactory(ILogger&lt;CustomDefaultTaskFlowFactory&gt; logger)
    ///     {
    ///         _logger = logger;
    ///     }
    ///     
    ///     public ITaskFlow Create(TaskFlowOptions options)
    ///     {
    ///         _logger.LogInformation("Creating TaskFlow with options: {Options}", options);
    ///         
    ///         // Create a custom task flow or wrap the standard one
    ///         var taskFlow = new TaskFlow(options);
    ///         return new LoggingTaskFlowWrapper(taskFlow, _logger);
    ///     }
    /// }
    /// 
    /// // Register the custom factory
    /// services.AddSingleton&lt;IDefaultTaskFlowFactory, CustomDefaultTaskFlowFactory&gt;();
    /// </code>
    /// <para>Usage in task flow factory:</para>
    /// <code>
    /// public class TaskFlowFactory : ITaskFlowFactory
    /// {
    ///     private readonly IDefaultTaskFlowFactory _defaultFactory;
    ///     
    ///     public TaskFlowFactory(IDefaultTaskFlowFactory defaultFactory)
    ///     {
    ///         _defaultFactory = defaultFactory;
    ///     }
    ///     
    ///     public ITaskFlow CreateTaskFlow(string? name = null)
    ///     {
    ///         var options = ResolveOptions(name);
    ///         
    ///         // Use default factory for creation
    ///         return _defaultFactory.Create(options);
    ///     }
    /// }
    /// </code>
    /// </example>
    public interface IDefaultTaskFlowFactory
    {
        /// <summary>
        /// Creates a new <see cref="ITaskFlow"/> instance with the specified configuration options.
        /// </summary>
        /// <param name="options">The configuration options to use for creating the task flow instance.</param>
        /// <returns>A new <see cref="ITaskFlow"/> instance configured with the specified options.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <c>null</c>.</exception>
        /// <remarks>
        /// <para>
        /// This method creates a task flow instance using the provided options directly, without any
        /// name-based resolution or additional configuration. The implementation should respect all
        /// settings specified in the options parameter.
        /// </para>
        /// <para>
        /// The created task flow should:
        /// </para>
        /// <list type="bullet">
        ///   <item>Honor all configuration settings from the options parameter</item>
        ///   <item>Be fully functional and ready for task enqueueing</item>
        ///   <item>Support proper disposal for resource cleanup</item>
        ///   <item>Maintain thread safety as required by the task flow contract</item>
        /// </list>
        /// <para>
        /// Implementations may choose to:
        /// </para>
        /// <list type="bullet">
        ///   <item>Create standard <see cref="TaskFlow"/> instances</item>
        ///   <item>Apply additional wrappers or decorators</item>
        ///   <item>Provide custom task flow implementations</item>
        ///   <item>Add logging, monitoring, or other cross-cutting concerns</item>
        /// </list>
        /// <para>
        /// The factory should not modify the provided options object, as it may be shared
        /// across multiple factory calls or contain immutable settings.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// public ITaskFlow Create(TaskFlowOptions options)
        /// {
        ///     // Validate options
        ///     if (options == null)
        ///         throw new ArgumentNullException(nameof(options));
        ///     
        ///     // Create the core task flow
        ///     var taskFlow = new TaskFlow(options);
        ///     
        ///     // Optionally add wrappers or decorators
        ///     if (options.EnableLogging)
        ///     {
        ///         taskFlow = new LoggingTaskFlowWrapper(taskFlow);
        ///     }
        ///     
        ///     return taskFlow;
        /// }
        /// </code>
        /// </example>
        ITaskFlow Create(TaskFlowOptions options);
    }
}