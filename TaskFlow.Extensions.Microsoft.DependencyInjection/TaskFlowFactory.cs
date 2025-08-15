namespace System.Threading.Tasks.Flow
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks.Flow.Annotations;
    using System.Threading.Tasks.Flow.Internal;

    /// <summary>
    /// Provides a comprehensive factory implementation for creating <see cref="ITaskFlow"/> instances with support for named configurations, custom factories, and scheduler chains.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="TaskFlowFactory"/> class serves as the central orchestrator for task flow creation in
    /// dependency injection scenarios. It coordinates multiple configuration sources to create appropriately
    /// configured task flow instances based on named configurations or default settings.
    /// </para>
    /// <para>
    /// The factory implements a sophisticated resolution system that supports:
    /// </para>
    /// <list type="bullet">
    ///   <item><strong>Named factories</strong> - Custom factory implementations for specific configurations</item>
    ///   <item><strong>Named options</strong> - Configuration objects specific to named instances</item>
    ///   <item><strong>Scheduler chains</strong> - Cross-cutting concern application through wrapper chains</item>
    ///   <item><strong>Default fallback</strong> - Consistent behavior when no named configuration exists</item>
    /// </list>
    /// <para>
    /// Resolution algorithm:
    /// </para>
    /// <list type="number">
    ///   <item><strong>Name normalization</strong> - Null names are converted to empty strings</item>
    ///   <item><strong>Configuration lookup</strong> - Search for named factories, options, and chains</item>
    ///   <item><strong>Options resolution</strong> - Use named options or fall back to defaults</item>
    ///   <item><strong>Factory selection</strong> - Use named factory or default factory for creation</item>
    ///   <item><strong>Chain application</strong> - Apply scheduler chains if configured</item>
    ///   <item><strong>Wrapper creation</strong> - Create ownership wrapper if chains are applied</item>
    /// </list>
    /// <para>
    /// The factory is designed to work seamlessly with Microsoft's dependency injection container
    /// and supports multiple named configurations within the same application. This enables different
    /// parts of an application to use task flows with different characteristics and behaviors.
    /// </para>
    /// </remarks>
    /// <example>
    /// <para>Basic factory registration and usage:</para>
    /// <code>
    /// // Register TaskFlow services in dependency injection
    /// services.AddTaskFlow();
    /// 
    /// // Use the factory
    /// public class DocumentProcessor
    /// {
    ///     private readonly ITaskFlowFactory _factory;
    ///     
    ///     public DocumentProcessor(ITaskFlowFactory factory)
    ///     {
    ///         _factory = factory;
    ///     }
    ///     
    ///     public async Task ProcessDocumentAsync()
    ///     {
    ///         using var taskFlow = _factory.CreateTaskFlow();
    ///         await taskFlow.Enqueue(() =&gt; ProcessDocumentContentAsync());
    ///     }
    /// }
    /// </code>
    /// <para>Named configuration setup:</para>
    /// <code>
    /// // Register multiple named configurations
    /// services.AddTaskFlow("database", new TaskFlowOptions { ... });
    /// services.AddTaskFlow("api", new TaskFlowOptions { ... });
    /// services.AddTaskFlow("background", 
    ///     configureSchedulerChain: (scheduler, provider) =&gt; 
    ///         scheduler.WithTimeout(TimeSpan.FromMinutes(30)));
    /// 
    /// // Use named configurations
    /// var databaseFlow = factory.CreateTaskFlow("database");
    /// var apiFlow = factory.CreateTaskFlow("api");
    /// var backgroundFlow = factory.CreateTaskFlow("background"); // With 30-minute timeout
    /// </code>
    /// <para>Custom factory and chain integration:</para>
    /// <code>
    /// // Custom named factory
    /// public class UiTaskFlowFactory : INamedTaskFlowFactory
    /// {
    ///     public string Name =&gt; "ui";
    ///     public ITaskFlow Create(TaskFlowOptions options) =&gt; new CurrentThreadTaskFlow(options);
    /// }
    /// 
    /// // Custom chain configuration
    /// public class LoggingChain : INamedConfigureTaskFlowChain
    /// {
    ///     public string Name =&gt; "ui";
    ///     public ITaskScheduler ConfigureChain(ITaskScheduler scheduler) =&gt;
    ///         scheduler.OnError(ex =&gt; _logger.LogError(ex, "UI operation failed"));
    /// }
    /// 
    /// // Registration
    /// services.AddSingleton&lt;INamedTaskFlowFactory, UiTaskFlowFactory&gt;();
    /// services.AddSingleton&lt;INamedConfigureTaskFlowChain, LoggingChain&gt;();
    /// 
    /// // Usage creates CurrentThreadTaskFlow with error logging
    /// var uiFlow = factory.CreateTaskFlow("ui");
    /// </code>
    /// </example>
    public class TaskFlowFactory : ITaskFlowFactory
    {
        private readonly IEnumerable<INamedTaskFlowFactory> _namedTaskFlowFactories;
        private readonly IEnumerable<INamedConfigureTaskFlowChain> _namedConfigureTaskFlowChains;
        private readonly IEnumerable<INamedConfigureTaskFlowOptions> _namedConfigureTaskFlowOptions;
        private readonly IDefaultTaskFlowFactory _defaultTaskFlowFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="TaskFlowFactory"/> class with the specified configuration dependencies.
        /// </summary>
        /// <param name="namedTaskFlowFactories">A collection of named task flow factories for custom creation logic.</param>
        /// <param name="namedConfigureTaskFlowChains">A collection of named scheduler chain configurations for applying cross-cutting concerns.</param>
        /// <param name="namedConfigureTaskFlowOptions">A collection of named options configurations for different task flow instances.</param>
        /// <param name="defaultTaskFlowFactory">The default factory to use when no named factory is available.</param>
        /// <exception cref="ArgumentNullException">Thrown when any of the parameters is <c>null</c>.</exception>
        /// <remarks>
        /// <para>
        /// This constructor sets up the factory with all necessary dependencies for comprehensive task flow creation.
        /// The dependencies are typically provided by the dependency injection container and represent the complete
        /// configuration space for task flow creation.
        /// </para>
        /// <para>
        /// Collection parameters can be empty but should not be null:
        /// </para>
        /// <list type="bullet">
        ///   <item><paramref name="namedTaskFlowFactories"/> - Empty collection means no named factories are registered</item>
        ///   <item><paramref name="namedConfigureTaskFlowChains"/> - Empty collection means no scheduler chains are configured</item>
        ///   <item><paramref name="namedConfigureTaskFlowOptions"/> - Empty collection means no named options are configured</item>
        /// </list>
        /// <para>
        /// The <paramref name="defaultTaskFlowFactory"/> is required and serves as the fallback for creating
        /// task flows when no named factory is available for a particular name.
        /// </para>
        /// </remarks>
        public TaskFlowFactory(
            IEnumerable<INamedTaskFlowFactory> namedTaskFlowFactories,
            IEnumerable<INamedConfigureTaskFlowChain> namedConfigureTaskFlowChains,
            IEnumerable<INamedConfigureTaskFlowOptions> namedConfigureTaskFlowOptions,
            IDefaultTaskFlowFactory defaultTaskFlowFactory)
        {
            Argument.NotNull(namedTaskFlowFactories);
            Argument.NotNull(namedConfigureTaskFlowChains);
            Argument.NotNull(namedConfigureTaskFlowOptions);
            Argument.NotNull(defaultTaskFlowFactory);

            _namedTaskFlowFactories = namedTaskFlowFactories;
            _namedConfigureTaskFlowChains = namedConfigureTaskFlowChains;
            _namedConfigureTaskFlowOptions = namedConfigureTaskFlowOptions;
            _defaultTaskFlowFactory = defaultTaskFlowFactory;
        }

        /// <summary>
        /// Creates a new <see cref="ITaskFlow"/> instance using the configuration associated with the specified name.
        /// </summary>
        /// <param name="name">
        /// The optional name identifying which configuration to use. If <c>null</c> or empty, 
        /// the default configuration is used.
        /// </param>
        /// <returns>
        /// A new <see cref="ITaskFlow"/> instance configured according to the specified name or default settings.
        /// The returned instance may be wrapped with scheduler chains if configured.
        /// </returns>
        /// <remarks>
        /// <para>
        /// This method implements the core factory logic by orchestrating the resolution and application
        /// of named configurations. The creation process follows these steps:
        /// </para>
        /// <list type="number">
        ///   <item><strong>Name normalization</strong> - Converts null to empty string for consistent lookup</item>
        ///   <item><strong>Configuration resolution</strong> - Searches for named factories, chains, and options</item>
        ///   <item><strong>Options determination</strong> - Uses named options or defaults to TaskFlowOptions.Default</item>
        ///   <item><strong>Base creation</strong> - Creates task flow using named factory or default factory</item>
        ///   <item><strong>Chain application</strong> - Applies scheduler chain if one is configured for the name</item>
        ///   <item><strong>Wrapper creation</strong> - Wraps with ownership wrapper if chains were applied</item>
        /// </list>
        /// <para>
        /// Resolution behavior:
        /// </para>
        /// <list type="bullet">
        ///   <item><strong>Single match expected</strong> - Uses SingleOrDefault() to find configurations, expects at most one match per name</item>
        ///   <item><strong>Graceful fallback</strong> - Missing named configurations result in default behavior rather than errors</item>
        ///   <item><strong>Chain independence</strong> - Chains are applied regardless of whether a named factory was used</item>
        /// </list>
        /// <para>
        /// The method creates a new task flow instance on each call - it does not cache or reuse instances.
        /// Callers are responsible for properly disposing the returned task flow instances.
        /// </para>
        /// <para>
        /// If multiple configurations are registered with the same name, the SingleOrDefault() call will
        /// throw an exception. This is by design to prevent ambiguous configuration scenarios.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // Create with default configuration
        /// var defaultFlow = factory.CreateTaskFlow();
        /// var alsoDefaultFlow = factory.CreateTaskFlow("");
        /// var nullDefaultFlow = factory.CreateTaskFlow(null);
        /// 
        /// // Create with named configuration
        /// var databaseFlow = factory.CreateTaskFlow("database");
        /// var apiFlow = factory.CreateTaskFlow("api");
        /// 
        /// // All instances should be disposed when done
        /// using var flow = factory.CreateTaskFlow("background");
        /// await flow.Enqueue(() =&gt; DoWorkAsync());
        /// </code>
        /// </example>
        public ITaskFlow CreateTaskFlow(string? name = null)
        {
            name ??= string.Empty;

            var namedFactory = GetByName(_namedTaskFlowFactories, name).SingleOrDefault();
            var configureTaskFlowChains = GetByName(_namedConfigureTaskFlowChains, name).SingleOrDefault();
            var configureTaskFlowOptions = GetByName(_namedConfigureTaskFlowOptions, name).SingleOrDefault();

            var options = configureTaskFlowOptions?.Configure() ?? TaskFlowOptions.Default;

            var baseTaskFlow = namedFactory != null
                ? namedFactory.Create(options)
                : _defaultTaskFlowFactory.Create(options);

            if (configureTaskFlowChains == null)
            {
                return baseTaskFlow;
            }

            var chainedTaskScheduler = configureTaskFlowChains.ConfigureChain(baseTaskFlow);
            return new TaskFlowOwnershipWrapper(baseTaskFlow, chainedTaskScheduler);
        }

        private static IEnumerable<INamedTaskFlowFactory> GetByName(IEnumerable<INamedTaskFlowFactory> items, string name)
        {
            return items.Where(item => item.Name == name);
        }

        private static IEnumerable<INamedConfigureTaskFlowChain> GetByName(IEnumerable<INamedConfigureTaskFlowChain> items, string name)
        {
            return items.Where(item => item.Name == name);
        }

        private static IEnumerable<INamedConfigureTaskFlowOptions> GetByName(IEnumerable<INamedConfigureTaskFlowOptions> items, string name)
        {
            return items.Where(item => item.Name == name);
        }
    }
}