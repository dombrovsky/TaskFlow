namespace System.Threading.Tasks.Flow
{
    /// <summary>
    /// Defines a factory contract for creating <see cref="ITaskFlow"/> instances with optional naming support.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="ITaskFlowFactory"/> interface provides a standardized way to create task flow instances
    /// within dependency injection containers. It supports named instances, allowing multiple task flow
    /// configurations to coexist within the same application.
    /// </para>
    /// <para>
    /// This factory interface is the primary entry point for creating task flows in dependency injection
    /// scenarios. It abstracts the complexity of:
    /// </para>
    /// <list type="bullet">
    ///   <item>Named configuration resolution</item>
    ///   <item>Custom factory delegation</item>
    ///   <item>Option configuration</item>
    ///   <item>Scheduler chain configuration</item>
    ///   <item>Default factory fallback</item>
    /// </list>
    /// <para>
    /// The factory supports multiple configuration patterns:
    /// </para>
    /// <list type="bullet">
    ///   <item><strong>Default instances</strong> - Created with default options when no name is specified</item>
    ///   <item><strong>Named instances</strong> - Created with specific configurations based on the provided name</item>
    ///   <item><strong>Custom factories</strong> - Delegate to user-provided factory implementations</item>
    ///   <item><strong>Chained schedulers</strong> - Apply scheduler wrapper chains for cross-cutting concerns</item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <para>Basic factory usage in dependency injection:</para>
    /// <code>
    /// // Register TaskFlow services
    /// services.AddTaskFlow();
    /// 
    /// // Inject and use the factory
    /// public class SomeService
    /// {
    ///     private readonly ITaskFlowFactory _taskFlowFactory;
    ///     
    ///     public SomeService(ITaskFlowFactory taskFlowFactory)
    ///     {
    ///         _taskFlowFactory = taskFlowFactory;
    ///     }
    ///     
    ///     public async Task ProcessDataAsync()
    ///     {
    ///         using var taskFlow = _taskFlowFactory.CreateTaskFlow();
    ///         await taskFlow.Enqueue(() => DoWorkAsync());
    ///     }
    /// }
    /// </code>
    /// </example>
    public interface ITaskFlowFactory
    {
        /// <summary>
        /// Creates a new <see cref="ITaskFlow"/> instance with the specified name or default configuration.
        /// </summary>
        /// <param name="name">
        /// The optional name identifying which configuration to use. If <c>null</c> or empty, 
        /// the default configuration is used.
        /// </param>
        /// <returns>
        /// A new <see cref="ITaskFlow"/> instance configured according to the specified name or default settings.
        /// </returns>
        /// <remarks>
        /// <para>
        /// This method creates task flow instances based on the configuration associated with the provided name.
        /// The factory resolves the configuration through the following precedence:
        /// </para>
        /// <list type="number">
        ///   <item><strong>Named factory</strong> - If a custom factory is registered for the name, it is used</item>
        ///   <item><strong>Default factory</strong> - If no named factory exists, the default factory is used</item>
        ///   <item><strong>Options configuration</strong> - Named options are applied if registered</item>
        ///   <item><strong>Scheduler chains</strong> - Named scheduler chains are applied if registered</item>
        ///   <item><strong>Fallback</strong> - Default options and configuration are used if nothing else is found</item>
        /// </list>
        /// <para>
        /// The name parameter supports:
        /// </para>
        /// <list type="bullet">
        ///   <item><c>null</c> - Uses default configuration (equivalent to empty string)</item>
        ///   <item>Empty string - Uses default configuration</item>
        ///   <item>Named string - Uses configuration registered with that specific name</item>
        /// </list>
        /// <para>
        /// Created task flows should be properly disposed when no longer needed to ensure
        /// resource cleanup and proper shutdown of any background operations.
        /// </para>
        /// <para>
        /// If multiple configurations are registered with the same name, the behavior depends
        /// on the specific registration order and DI container implementation. Generally,
        /// the last registered configuration takes precedence.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // Create default task flow
        /// using var defaultFlow = factory.CreateTaskFlow();
        /// 
        /// // Create named task flow
        /// using var namedFlow = factory.CreateTaskFlow("background-processor");
        /// 
        /// // Both are equivalent for default configuration
        /// using var flow1 = factory.CreateTaskFlow(null);
        /// using var flow2 = factory.CreateTaskFlow("");
        /// using var flow3 = factory.CreateTaskFlow();
        /// </code>
        /// </example>
        ITaskFlow CreateTaskFlow(string? name = null);
    }
}