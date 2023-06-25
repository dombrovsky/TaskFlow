namespace System.Threading.Tasks.Flow
{
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;
    using System.Threading.Tasks.Flow.Annotations;
    using System.Threading.Tasks.Flow.Internal;
    using System.Xml.Linq;

    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddTaskFlow(this IServiceCollection services)
        {
            return AddTaskFlow(services, name: null);
        }

        public static IServiceCollection AddTaskFlow(this IServiceCollection services, TaskFlowOptions options)
        {
            Argument.NotNull(options);

            return AddTaskFlow(services, name: null, baseTaskFlowFactory: null, _ => options, configureSchedulerChain: null);
        }

        public static IServiceCollection AddTaskFlow(this IServiceCollection services, string name, TaskFlowOptions options)
        {
            Argument.NotEmpty(name);
            Argument.NotNull(options);

            return AddTaskFlow(services, name, baseTaskFlowFactory: null, _ => options, configureSchedulerChain: null);
        }

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

            services.TryAddScoped<ITaskFlow>(provider => provider.GetRequiredService<ITaskFlowFactory>().CreateTaskFlow(name));
            services.TryAddScoped<ITaskScheduler>(provider => provider.GetRequiredService<ITaskFlow>());

            return services;
        }
    }
}
