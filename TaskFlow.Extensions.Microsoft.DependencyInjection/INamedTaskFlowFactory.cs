namespace System.Threading.Tasks.Flow
{
    /// <summary>
    /// Defines a named factory contract for creating <see cref="ITaskFlow"/> instances with specific configurations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="INamedTaskFlowFactory"/> interface extends the factory pattern to support named
    /// configurations, allowing multiple task flow factories to coexist within the same dependency
    /// injection container. Each factory is associated with a specific name and provides custom
    /// creation logic for that particular configuration.
    /// </para>
    /// <para>
    /// Named factories enable:
    /// </para>
    /// <list type="bullet">
    ///   <item><strong>Multiple configurations</strong> - Different task flow types for different purposes</item>
    ///   <item><strong>Specialized implementations</strong> - Custom task flow types for specific scenarios</item>
    ///   <item><strong>Environment-specific behavior</strong> - Different implementations for testing vs. production</item>
    ///   <item><strong>Feature-specific optimizations</strong> - Tailored task flows for specific application features</item>
    /// </list>
    /// <para>
    /// The factory system works hierarchically:
    /// </para>
    /// <list type="number">
    ///   <item>Named factories are checked first when creating task flows</item>
    ///   <item>If no named factory exists, the default factory is used</item>
    ///   <item>Options and scheduler chains can still be applied to named factory results</item>
    /// </list>
    /// <para>
    /// Common use cases include:
    /// </para>
    /// <list type="bullet">
    ///   <item>Background processing with dedicated thread task flows</item>
    ///   <item>UI operations with current thread task flows</item>
    ///   <item>High-throughput scenarios with custom parallel implementations</item>
    ///   <item>Testing scenarios with mock or instrumented task flows</item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <para>Creating a custom named factory:</para>
    /// <code>
    /// public class BackgroundTaskFlowFactory : INamedTaskFlowFactory
    /// {
    ///     public string Name =&gt; "background";
    ///     
    ///     public ITaskFlow Create(TaskFlowOptions options)
    ///     {
    ///         // Create a dedicated thread task flow for background processing
    ///         return new DedicatedThreadTaskFlow(options);
    ///     }
    /// }
    /// 
    /// public class UiTaskFlowFactory : INamedTaskFlowFactory
    /// {
    ///     public string Name =&gt; "ui";
    ///     
    ///     public ITaskFlow Create(TaskFlowOptions options)
    ///     {
    ///         // Create a current thread task flow for UI operations
    ///         return new CurrentThreadTaskFlow(options);
    ///     }
    /// }
    /// </code>
    /// <para>Registering named factories:</para>
    /// <code>
    /// // Register multiple named factories
    /// services.AddSingleton&lt;INamedTaskFlowFactory, BackgroundTaskFlowFactory&gt;();
    /// services.AddSingleton&lt;INamedTaskFlowFactory, UiTaskFlowFactory&gt;();
    /// 
    /// // Register using delegate factory
    /// services.AddTaskFlow("database", (provider, options) =&gt; 
    ///     new TaskFlow(options with { MaxConcurrency = 1 }));
    /// </code>
    /// <para>Using named factories:</para>
    /// <code>
    /// public class ApplicationService
    /// {
    ///     private readonly ITaskFlowFactory _factory;
    ///     
    ///     public ApplicationService(ITaskFlowFactory factory)
    ///     {
    ///         _factory = factory;
    ///     }
    ///     
    ///     public async Task ProcessInBackgroundAsync()
    ///     {
    ///         using var taskFlow = _factory.CreateTaskFlow("background");
    ///         await taskFlow.Enqueue(() =&gt; DoBackgroundWorkAsync());
    ///     }
    ///     
    ///     public async Task UpdateUiAsync()
    ///     {
    ///         using var taskFlow = _factory.CreateTaskFlow("ui");
    ///         await taskFlow.Enqueue(() =&gt; UpdateUserInterfaceAsync());
    ///     }
    /// }
    /// </code>
    /// </example>
    public interface INamedTaskFlowFactory
    {
        /// <summary>
        /// Gets the name that identifies this factory configuration.
        /// </summary>
        /// <value>
        /// A string that uniquely identifies this factory within the dependency injection container.
        /// This name is used to resolve which factory should be used when creating named task flow instances.
        /// </value>
        /// <remarks>
        /// <para>
        /// The name serves as the key for factory resolution in the dependency injection system.
        /// When <see cref="ITaskFlowFactory.CreateTaskFlow(string?)"/> is called with a specific name,
        /// the factory system searches for a registered <see cref="INamedTaskFlowFactory"/> with a
        /// matching <see cref="Name"/> property.
        /// </para>
        /// <para>
        /// Name matching considerations:
        /// </para>
        /// <list type="bullet">
        ///   <item><strong>Case sensitivity</strong> - Names are typically case-sensitive</item>
        ///   <item><strong>Uniqueness</strong> - Each factory should have a unique name within the container</item>
        ///   <item><strong>Stability</strong> - The name should remain constant throughout the factory's lifetime</item>
        ///   <item><strong>Null handling</strong> - Names should not be null (use empty string for default if needed)</item>
        /// </list>
        /// <para>
        /// If multiple factories are registered with the same name, the behavior depends on the
        /// dependency injection container implementation, but typically the last registered factory
        /// will be used.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// public class DatabaseTaskFlowFactory : INamedTaskFlowFactory
        /// {
        ///     // This factory will be used when CreateTaskFlow("database") is called
        ///     public string Name =&gt; "database";
        ///     
        ///     public ITaskFlow Create(TaskFlowOptions options)
        ///     {
        ///         // Create task flow optimized for database operations
        ///         return new TaskFlow(options with { MaxConcurrency = 1 });
        ///     }
        /// }
        /// </code>
        /// </example>
        string Name { get; }

        /// <summary>
        /// Creates a new <see cref="ITaskFlow"/> instance with the specified configuration options.
        /// </summary>
        /// <param name="options">The configuration options to use for creating the task flow instance.</param>
        /// <returns>A new <see cref="ITaskFlow"/> instance configured according to this factory's implementation and the provided options.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <c>null</c>.</exception>
        /// <remarks>
        /// <para>
        /// This method implements the core creation logic for this named factory. Unlike the default
        /// factory, named factories can provide completely custom implementations, different task flow
        /// types, or specialized configurations based on their intended purpose.
        /// </para>
        /// <para>
        /// Implementation guidelines:
        /// </para>
        /// <list type="bullet">
        ///   <item><strong>Options respect</strong> - Should honor relevant settings from the options parameter</item>
        ///   <item><strong>Consistent behavior</strong> - Should create task flows with predictable characteristics</item>
        ///   <item><strong>Resource management</strong> - Created task flows should support proper disposal</item>
        ///   <item><strong>Thread safety</strong> - The factory method should be thread-safe for concurrent calls</item>
        /// </list>
        /// <para>
        /// Named factories can:
        /// </para>
        /// <list type="bullet">
        ///   <item>Create different task flow implementations (e.g., <see cref="DedicatedThreadTaskFlow"/>, <see cref="CurrentThreadTaskFlow"/>)</item>
        ///   <item>Apply specific option modifications or defaults</item>
        ///   <item>Add decorators or wrappers for logging, monitoring, or other concerns</item>
        ///   <item>Integrate with external systems or configuration sources</item>
        /// </list>
        /// <para>
        /// The factory should not modify the provided options object directly, as it may be shared
        /// across multiple factory calls. If option modifications are needed, create a copy or use
        /// immutable update patterns.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// public ITaskFlow Create(TaskFlowOptions options)
        /// {
        ///     // Validate input
        ///     if (options == null)
        ///         throw new ArgumentNullException(nameof(options));
        ///     
        ///     // Create specialized task flow for this factory's purpose
        ///     ITaskFlow taskFlow = Name switch
        ///     {
        ///         "background" =&gt; new DedicatedThreadTaskFlow(options),
        ///         "ui" =&gt; new CurrentThreadTaskFlow(options),
        ///         "parallel" =&gt; new TaskFlow(options with { MaxConcurrency = Environment.ProcessorCount }),
        ///         _ =&gt; new TaskFlow(options)
        ///     };
        ///     
        ///     // Add any common decorators
        ///     if (_enableLogging)
        ///     {
        ///         taskFlow = new LoggingTaskFlowWrapper(taskFlow, _logger);
        ///     }
        ///     
        ///     return taskFlow;
        /// }
        /// </code>
        /// </example>
        ITaskFlow Create(TaskFlowOptions options);
    }
}