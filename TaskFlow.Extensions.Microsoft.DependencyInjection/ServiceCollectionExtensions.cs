namespace System.Threading.Tasks.Flow
{
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;
    using System.Threading.Tasks.Flow.Annotations;
    using System.Threading.Tasks.Flow.Internal;

    /// <summary>
    /// Provides extension methods for <see cref="IServiceCollection"/> to register TaskFlow services and configurations in dependency injection containers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="ServiceCollectionExtensions"/> class serves as the primary entry point for integrating
    /// TaskFlow with Microsoft's dependency injection system. It provides a comprehensive set of extension methods
    /// that enable registration of task flow services with various configuration options and customization points.
    /// </para>
    /// <para>
    /// Key registration features:
    /// </para>
    /// <list type="bullet">
    ///   <item><strong>Default registration</strong> - Simple registration with default settings</item>
    ///   <item><strong>Options-based registration</strong> - Registration with explicit configuration options</item>
    ///   <item><strong>Named configurations</strong> - Support for multiple named task flow configurations</item>
    ///   <item><strong>Custom factories</strong> - Integration with custom task flow creation logic</item>
    ///   <item><strong>Scheduler chains</strong> - Configuration of cross-cutting concerns through wrapper chains</item>
    /// </list>
    /// <para>
    /// Service lifetime patterns:
    /// </para>
    /// <list type="bullet">
    ///   <item><strong>Singleton factories</strong> - Configuration and factory services are registered as singletons</item>
    ///   <item><strong>Scoped instances</strong> - Task flow instances are scoped to dependency injection scopes</item>
    ///   <item><strong>Automatic disposal</strong> - Task flows are automatically disposed when scopes end</item>
    /// </list>
    /// <para>
    /// The extension methods register the following services:
    /// </para>
    /// <list type="bullet">
    ///   <item><see cref="ITaskFlowFactory"/> - Factory for creating task flow instances</item>
    ///   <item><see cref="ITaskScheduler"/> - Task scheduler interface implemented by scoped task flows</item>
    ///   <item><see cref="ITaskFlowInfo"/> - Information interface implemented by scoped task flows</item>
    ///   <item><see cref="IDefaultTaskFlowFactory"/> - Default factory for standard task flow creation</item>
    /// </list>
    /// <para>
    /// Note that <see cref="ITaskFlow"/> itself is not registered directly. Instead, scoped wrapper
    /// instances provide the <see cref="ITaskScheduler"/> and <see cref="ITaskFlowInfo"/> interfaces,
    /// allowing controlled access to task flow functionality while ensuring proper lifecycle management.
    /// </para>
    /// </remarks>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds TaskFlow services to the specified <see cref="IServiceCollection"/> with default configuration.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> to add TaskFlow services to.</param>
        /// <returns>The <see cref="IServiceCollection"/> for method chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is <c>null</c>.</exception>
        /// <remarks>
        /// <para>
        /// This is the simplest registration method that adds TaskFlow services with default settings.
        /// It registers all necessary services for basic TaskFlow functionality without any custom configuration.
        /// </para>
        /// <para>
        /// Services registered:
        /// </para>
        /// <list type="bullet">
        ///   <item><see cref="ITaskFlowFactory"/> as singleton</item>
        ///   <item><see cref="IDefaultTaskFlowFactory"/> as singleton</item>
        ///   <item><see cref="ITaskScheduler"/> as scoped</item>
        ///   <item><see cref="ITaskFlowInfo"/> as scoped</item>
        /// </list>
        /// <para>
        /// The scoped services provide access to a task flow instance that is created per dependency injection
        /// scope and automatically disposed when the scope ends. This ensures proper resource management
        /// and clean shutdown of background operations.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// public void ConfigureServices(IServiceCollection services)
        /// {
        ///     services.AddTaskFlow();
        ///     
        ///     services.AddScoped&lt;MyService&gt;();
        /// }
        /// 
        /// public class MyService
        /// {
        ///     private readonly ITaskScheduler _scheduler;
        ///     
        ///     public MyService(ITaskScheduler scheduler)
        ///     {
        ///         _scheduler = scheduler;
        ///     }
        ///     
        ///     public Task DoWorkAsync() =&gt; _scheduler.Enqueue(() =&gt; ProcessDataAsync());
        /// }
        /// </code>
        /// </example>
        public static IServiceCollection AddTaskFlow(this IServiceCollection services)
        {
            return AddTaskFlow(services, name: null);
        }

        /// <summary>
        /// Adds TaskFlow services to the specified <see cref="IServiceCollection"/> with the specified options.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> to add TaskFlow services to.</param>
        /// <param name="options">The <see cref="TaskFlowOptions"/> to use for configuring the default task flow instances.</param>
        /// <returns>The <see cref="IServiceCollection"/> for method chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="options"/> is <c>null</c>.</exception>
        /// <remarks>
        /// <para>
        /// This method registers TaskFlow services with explicit configuration options that will be used
        /// for all default task flow instances. The options are applied to the default (unnamed) configuration.
        /// </para>
        /// <para>
        /// The provided options object is used directly, so it should not be modified after registration.
        /// If you need different options for different scenarios, consider using named configurations instead.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// services.AddTaskFlow(new TaskFlowOptions
        /// {
        ///     SynchronousDisposeTimeout = TimeSpan.FromSeconds(30)
        /// });
        /// </code>
        /// </example>
        public static IServiceCollection AddTaskFlow(this IServiceCollection services, TaskFlowOptions options)
        {
            Argument.NotNull(options);

            return AddTaskFlow(services, name: null, baseTaskFlowFactory: null, _ => options, configureSchedulerChain: null);
        }

        /// <summary>
        /// Adds TaskFlow services to the specified <see cref="IServiceCollection"/> with a named configuration and specified options.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> to add TaskFlow services to.</param>
        /// <param name="name">The name to associate with this configuration. Must not be <c>null</c> or empty.</param>
        /// <param name="options">The <see cref="TaskFlowOptions"/> to use for configuring task flow instances with this name.</param>
        /// <returns>The <see cref="IServiceCollection"/> for method chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="options"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is <c>null</c> or empty.</exception>
        /// <remarks>
        /// <para>
        /// This method registers a named configuration that can be accessed by calling
        /// <see cref="ITaskFlowFactory.CreateTaskFlow(string?)"/> with the specified name. This enables
        /// different parts of an application to use task flows with different configurations.
        /// </para>
        /// <para>
        /// Named configurations are additive - you can register multiple named configurations and they
        /// will coexist without interfering with each other or the default configuration.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // Register database configuration for single-threaded access
        /// services.AddTaskFlow("database", new TaskFlowOptions { SynchronousDisposeTimeout = TimeSpan.FromSeconds(30) });
        /// 
        /// // Register API configuration for high concurrency
        /// services.AddTaskFlow("api", new TaskFlowOptions { SynchronousDisposeTimeout = TimeSpan.FromSeconds(10) });
        /// 
        /// // Usage
        /// var databaseFlow = factory.CreateTaskFlow("database");
        /// var apiFlow = factory.CreateTaskFlow("api");
        /// </code>
        /// </example>
        public static IServiceCollection AddTaskFlow(this IServiceCollection services, string name, TaskFlowOptions options)
        {
            Argument.NotEmpty(name);
            Argument.NotNull(options);

            return AddTaskFlow(services, name, baseTaskFlowFactory: null, _ => options, configureSchedulerChain: null);
        }

        /// <summary>
        /// Adds TaskFlow services to the specified <see cref="IServiceCollection"/> with comprehensive configuration options.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> to add TaskFlow services to.</param>
        /// <param name="name">The optional name to associate with this configuration. If <c>null</c>, applies to the default configuration.</param>
        /// <param name="baseTaskFlowFactory">An optional custom factory function for creating the base task flow instances.</param>
        /// <param name="configureOptions">An optional function for configuring task flow options based on the service provider.</param>
        /// <param name="configureSchedulerChain">An optional function for configuring the scheduler chain to apply cross-cutting concerns.</param>
        /// <returns>The <see cref="IServiceCollection"/> for method chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is <c>null</c>.</exception>
        /// <remarks>
        /// <para>
        /// This is the most comprehensive registration method that provides full control over task flow creation
        /// and configuration. It allows customization of every aspect of the task flow creation process through
        /// the provided delegate functions.
        /// </para>
        /// <para>
        /// Configuration precedence and application order:
        /// </para>
        /// <list type="number">
        ///   <item><paramref name="configureOptions"/> is called to determine the options</item>
        ///   <item><paramref name="baseTaskFlowFactory"/> is called to create the base task flow (or default factory if null)</item>
        ///   <item><paramref name="configureSchedulerChain"/> is called to wrap the scheduler with additional functionality</item>
        /// </list>
        /// <para>
        /// All delegate functions receive the <see cref="IServiceProvider"/> as a parameter, allowing them to
        /// access other registered services for configuration purposes. This enables complex configuration
        /// scenarios based on runtime conditions, configuration files, or other services.
        /// </para>
        /// <para>
        /// Factory and chain delegates are only called when task flows are actually created, not during
        /// service registration. This enables lazy evaluation and access to scoped services if needed.
        /// </para>
        /// </remarks>
        /// <example>
        /// <para>Custom factory with dependency injection:</para>
        /// <code>
        /// services.AddTaskFlow("custom",
        ///     baseTaskFlowFactory: (provider, options) =&gt; {
        ///         var logger = provider.GetRequiredService&lt;ILogger&lt;TaskFlow&gt;&gt;();
        ///         var taskFlow = new TaskFlow(options);
        ///         return new LoggingTaskFlowWrapper(taskFlow, logger);
        ///     });
        /// </code>
        /// <para>Dynamic options configuration:</para>
        /// <code>
        /// services.AddTaskFlow("adaptive",
        ///     configureOptions: provider =&gt; {
        ///         var config = provider.GetRequiredService&lt;IConfiguration&gt;();
        ///         var environment = provider.GetRequiredService&lt;IWebHostEnvironment&gt;();
        ///         
        ///         return new TaskFlowOptions
        ///         {
        ///             SynchronousDisposeTimeout = environment.IsDevelopment() ? TimeSpan.FromSeconds(30) : config.GetValue&lt;int&gt;("TaskFlow:SynchronousDisposeTimeout")
        ///         };
        ///     });
        /// </code>
        /// <para>Comprehensive scheduler chain:</para>
        /// <code>
        /// services.AddTaskFlow("robust",
        ///     configureSchedulerChain: (scheduler, provider) =&gt; {
        ///         var logger = provider.GetRequiredService&lt;ILogger&gt;();
        ///         var metrics = provider.GetRequiredService&lt;IMetrics&gt;();
        ///         var config = provider.GetRequiredService&lt;IConfiguration&gt;();
        ///         
        ///         var chain = scheduler
        ///             .WithOperationName("RobustOperation")
        ///             .WithTimeout(TimeSpan.FromSeconds(config.GetValue&lt;int&gt;("Timeout", 30)))
        ///             .OnError&lt;Exception&gt;(ex =&gt; {
        ///                 logger.LogError(ex, "Operation failed");
        ///                 metrics.IncrementCounter("errors");
        ///             });
        ///             
        ///         if (config.GetValue&lt;bool&gt;("EnableThrottling"))
        ///         {
        ///             chain = chain.WithDebounce(TimeSpan.FromMilliseconds(500));
        ///         }
        ///         
        ///         return chain;
        ///     });
        /// </code>
        /// </example>
        public static IServiceCollection AddTaskFlow(
            this IServiceCollection services,
            string? name,
            Func<IServiceProvider, TaskFlowOptions, ITaskFlow>? baseTaskFlowFactory = null,
            Func<IServiceProvider, TaskFlowOptions>? configureOptions = null,
            Func<ITaskScheduler, IServiceProvider, ITaskScheduler>? configureSchedulerChain = null)
        {
            Argument.NotNull(services);

            name ??= string.Empty;

            services.TryAddSingleton<ITaskFlowFactory, TaskFlowFactory>();
            services.TryAddSingleton<IDefaultTaskFlowFactory, DefaultTaskFlowFactory>();

            if (configureOptions != null)
            {
                services.AddSingleton<INamedConfigureTaskFlowOptions>(provider => new DelegateNamedConfigureTaskFlowOptions(provider, name, configureOptions));
            }

            if (baseTaskFlowFactory != null)
            {
                services.AddSingleton<INamedTaskFlowFactory>(provider => new DelegateNamedTaskFlowFactory(provider, name, baseTaskFlowFactory));
            }

            if (configureSchedulerChain != null)
            {
                services.AddSingleton<INamedConfigureTaskFlowChain>(provider => new DelegateNamedConfigureTaskFlowChain(provider, name, configureSchedulerChain));
            }

            services.TryAddScoped<TaskFlowWrapper>(provider => new TaskFlowWrapper(provider.GetRequiredService<ITaskFlowFactory>().CreateTaskFlow(name)));
            services.TryAddScoped<ITaskScheduler>(provider => provider.GetRequiredService<TaskFlowWrapper>());
            services.TryAddScoped<ITaskFlowInfo>(provider => provider.GetRequiredService<TaskFlowWrapper>());

            return services;
        }
    }
}
