namespace System.Threading.Tasks.Flow
{
    /// <summary>
    /// Defines a named configuration contract for applying scheduler chain modifications to named task flow instances.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="INamedConfigureTaskFlowChain"/> interface enables the configuration of scheduler wrapper chains
    /// for named task flow instances. This allows the application of cross-cutting concerns like error handling,
    /// timeouts, throttling, and other TaskFlow extensions to specific named instances.
    /// </para>
    /// <para>
    /// Scheduler chains provide:
    /// </para>
    /// <list type="bullet">
    ///   <item><strong>Cross-cutting concerns</strong> - Apply common functionality without modifying core logic</item>
    ///   <item><strong>Named customization</strong> - Different chains for different named instances</item>
    ///   <item><strong>Composition</strong> - Chain multiple schedulers together for complex behaviors</item>
    ///   <item><strong>Dependency injection integration</strong> - Access other services for chain configuration</item>
    /// </list>
    /// <para>
    /// The chain configuration system integrates with:
    /// </para>
    /// <list type="bullet">
    ///   <item><see cref="INamedTaskFlowFactory"/> - Chains are applied after factory creation</item>
    ///   <item><see cref="INamedConfigureTaskFlowOptions"/> - Options are used before chain application</item>
    ///   <item>TaskFlow extension methods - Chains can use any scheduler extension methods</item>
    /// </list>
    /// <para>
    /// Execution order:
    /// </para>
    /// <list type="number">
    ///   <item>Named or default factory creates the base task flow</item>
    ///   <item>Named chain configuration is applied to wrap the scheduler</item>
    ///   <item>The wrapped scheduler is used for all task operations</item>
    /// </list>
    /// <para>
    /// Common chain use cases:
    /// </para>
    /// <list type="bullet">
    ///   <item>Adding timeout protection to API call task flows</item>
    ///   <item>Applying error handling and logging to background processing</item>
    ///   <item>Adding throttling to prevent resource exhaustion</item>
    ///   <item>Implementing retry logic for unreliable operations</item>
    ///   <item>Adding monitoring and metrics collection</item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <para>Creating a named chain configuration:</para>
    /// <code>
    /// public class ApiTaskFlowChain : INamedConfigureTaskFlowChain
    /// {
    ///     private readonly ILogger&lt;ApiTaskFlowChain&gt; _logger;
    ///     private readonly IMetrics _metrics;
    ///     
    ///     public ApiTaskFlowChain(ILogger&lt;ApiTaskFlowChain&gt; logger, IMetrics metrics)
    ///     {
    ///         _logger = logger;
    ///         _metrics = metrics;
    ///     }
    ///     
    ///     public string Name =&gt; "api";
    ///     
    ///     public ITaskScheduler ConfigureChain(ITaskScheduler taskScheduler)
    ///     {
    ///         return taskScheduler
    ///             .WithOperationName("ApiOperation")
    ///             .WithTimeout(TimeSpan.FromSeconds(30))
    ///             .OnError&lt;HttpRequestException&gt;((sched, ex, name) =&gt; 
    ///                 _logger.LogWarning(ex, "HTTP error in {Operation}", name?.OperationName))
    ///             .OnError&lt;TimeoutException&gt;((sched, ex, name) =&gt; 
    ///                 _metrics.IncrementCounter("api.timeout", new { operation = name?.OperationName }));
    ///     }
    /// }
    /// </code>
    /// <para>Registering named chains:</para>
    /// <code>
    /// // Register using implementation
    /// services.AddSingleton&lt;INamedConfigureTaskFlowChain, ApiTaskFlowChain&gt;();
    /// 
    /// // Register using delegate
    /// services.AddTaskFlow("background", 
    ///     configureSchedulerChain: (scheduler, provider) =&gt; {
    ///         var logger = provider.GetRequiredService&lt;ILogger&gt;();
    ///         return scheduler
    ///             .WithOperationName("BackgroundWork")
    ///             .OnError(ex =&gt; logger.LogError(ex, "Background processing error"));
    ///     });
    /// </code>
    /// <para>Complex chain configuration:</para>
    /// <code>
    /// public ITaskScheduler ConfigureChain(ITaskScheduler taskScheduler)
    /// {
    ///     var baseScheduler = taskScheduler
    ///         .WithOperationName($"{Name}Operation")
    ///         .WithTimeout(TimeSpan.FromMinutes(5));
    ///     
    ///     // Add environment-specific behavior
    ///     if (_environment.IsProduction())
    ///     {
    ///         baseScheduler = baseScheduler
    ///             .WithDebounce(TimeSpan.FromSeconds(1))
    ///             .OnError&lt;Exception&gt;(ex =&gt; _telemetry.TrackException(ex));
    ///     }
    ///     else
    ///     {
    ///         baseScheduler = baseScheduler
    ///             .OnError&lt;Exception&gt;(ex =&gt; _console.WriteLine($"Error: {ex}"));
    ///     }
    ///     
    ///     return baseScheduler;
    /// }
    /// </code>
    /// </example>
    public interface INamedConfigureTaskFlowChain
    {
        /// <summary>
        /// Gets the name that identifies this scheduler chain configuration.
        /// </summary>
        /// <value>
        /// A string that uniquely identifies this chain configuration within the dependency injection container.
        /// This name is used to resolve which chain should be applied when creating named task flow instances.
        /// </value>
        /// <remarks>
        /// <para>
        /// The name serves as the key for chain resolution in the dependency injection system.
        /// When <see cref="ITaskFlowFactory.CreateTaskFlow(string?)"/> is called with a specific name,
        /// the factory system searches for a registered <see cref="INamedConfigureTaskFlowChain"/> with a
        /// matching <see cref="Name"/> property.
        /// </para>
        /// <para>
        /// Name coordination considerations:
        /// </para>
        /// <list type="bullet">
        ///   <item><strong>Consistency</strong> - Should match names used in factories and options for coordinated configuration</item>
        ///   <item><strong>Case sensitivity</strong> - Names are typically case-sensitive</item>
        ///   <item><strong>Uniqueness</strong> - Each chain configuration should have a unique name within the container</item>
        ///   <item><strong>Stability</strong> - The name should remain constant throughout the configuration's lifetime</item>
        /// </list>
        /// <para>
        /// The chain is applied after the base task flow is created (either by a named factory or the default factory)
        /// but before the task flow is returned to the calling code. This allows the chain to wrap the scheduler
        /// with additional functionality.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// public class DatabaseChain : INamedConfigureTaskFlowChain
        /// {
        ///     // This chain will be applied when CreateTaskFlow("database") is called
        ///     public string Name =&gt; "database";
        ///     
        ///     public ITaskScheduler ConfigureChain(ITaskScheduler taskScheduler)
        ///     {
        ///         return taskScheduler
        ///             .WithOperationName("DatabaseOperation")
        ///             .WithTimeout(TimeSpan.FromSeconds(30))
        ///             .OnError&lt;SqlException&gt;(ex =&gt; _logger.LogError(ex, "Database error"));
        ///     }
        /// }
        /// </code>
        /// </example>
        string Name { get; }

        /// <summary>
        /// Configures and returns a scheduler chain by wrapping the provided task scheduler with additional functionality.
        /// </summary>
        /// <param name="taskScheduler">The base task scheduler to wrap with additional functionality.</param>
        /// <returns>An <see cref="ITaskScheduler"/> that wraps the original scheduler with the configured chain.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="taskScheduler"/> is <c>null</c>.</exception>
        /// <remarks>
        /// <para>
        /// This method applies scheduler wrapper chains to add cross-cutting concerns and additional functionality
        /// to the base task scheduler. The method can chain multiple scheduler extensions together to create
        /// complex behaviors while maintaining the core scheduler interface.
        /// </para>
        /// <para>
        /// Chain configuration guidelines:
        /// </para>
        /// <list type="bullet">
        ///   <item><strong>Composition</strong> - Use TaskFlow extension methods to build the chain</item>
        ///   <item><strong>Order matters</strong> - The order of chained extensions affects behavior</item>
        ///   <item><strong>Preserve interface</strong> - Always return an ITaskScheduler implementation</item>
        ///   <item><strong>Resource management</strong> - Ensure proper disposal is supported throughout the chain</item>
        /// </list>
        /// <para>
        /// Available extension methods for chaining include:
        /// </para>
        /// <list type="bullet">
        ///   <item><see cref="AnnotatingTaskSchedulerExtensions.WithOperationName(ITaskScheduler, string)"/> - Add operation naming</item>
        ///   <item><see cref="TimeoutTaskSchedulerExtensions.WithTimeout(ITaskScheduler, TimeSpan)"/> - Add timeout protection</item>
        ///   <item><see cref="ThrottlingTaskSchedulerExtensions.WithDebounce(ITaskScheduler, TimeSpan, TimeProvider?)"/> - Add debouncing</item>
        ///   <item><see cref="ExceptionTaskSchedulerExtensions.OnError{TException}(ITaskScheduler, Action{TException})"/> - Add error handling</item>
        ///   <item><see cref="CancellationScopeTaskSchedulerExtensions.CreateCancellationScope(ITaskScheduler, CancellationToken)"/> - Add cancellation scopes</item>
        ///   <item><see cref="CancelPreviousTaskSchedulerExtensions.CreateCancelPrevious(ITaskScheduler)"/> - Add cancel-previous behavior</item>
        /// </list>
        /// <para>
        /// The method can access dependency injection services to configure the chain based on runtime conditions,
        /// configuration settings, or other application state.
        /// </para>
        /// <para>
        /// Chain execution order is determined by the order of method calls, with each extension wrapping
        /// the previous one. The outermost wrapper receives calls first, and the original scheduler
        /// receives calls last.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// public ITaskScheduler ConfigureChain(ITaskScheduler taskScheduler)
        /// {
        ///     if (taskScheduler == null)
        ///         throw new ArgumentNullException(nameof(taskScheduler));
        ///     
        ///     // Build a comprehensive chain for this named instance
        ///     var chain = taskScheduler
        ///         .WithOperationName(Name) // Add operation naming
        ///         .WithTimeout(TimeSpan.FromMinutes(2)); // Add timeout protection
        ///     
        ///     // Add environment-specific extensions
        ///     if (_configuration.GetValue&lt;bool&gt;("EnableThrottling"))
        ///     {
        ///         var interval = _configuration.GetValue&lt;TimeSpan&gt;("ThrottleInterval");
        ///         chain = chain.WithDebounce(interval);
        ///     }
        ///     
        ///     // Add error handling
        ///     chain = chain
        ///         .OnError&lt;TimeoutException&gt;((sched, ex, name) =&gt; 
        ///             _metrics.IncrementCounter("timeout", new { operation = name?.OperationName }))
        ///         .OnError&lt;Exception&gt;(ex =&gt; 
        ///             _logger.LogError(ex, "Error in {Name} operation", Name));
        ///     
        ///     return chain;
        /// }
        /// </code>
        /// </example>
        ITaskScheduler ConfigureChain(ITaskScheduler taskScheduler);
    }
}