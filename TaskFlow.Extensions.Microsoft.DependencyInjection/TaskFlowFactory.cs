namespace System.Threading.Tasks.Flow
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks.Flow.Annotations;
    using System.Threading.Tasks.Flow.Internal;

    public class TaskFlowFactory : ITaskFlowFactory
    {
        private readonly IEnumerable<INamedTaskFlowFactory> _namedTaskFlowFactories;
        private readonly IEnumerable<INamedConfigureTaskFlowChain> _namedConfigureTaskFlowChains;
        private readonly IEnumerable<INamedConfigureTaskFlowOptions> _namedConfigureTaskFlowOptions;
        private readonly IDefaultTaskFlowFactory _defaultTaskFlowFactory;

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

        private static IEnumerable<T> GetByName<T>(IEnumerable<T> items, string name)
            where T : IHaveName
        {
            return items.Where(item => item.Name == name);
        }
    }
}