namespace System.Threading.Tasks.Flow.Internal
{
    internal sealed class DelegateNamedConfigureTaskFlowOptions : INamedConfigureTaskFlowOptions
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly Func<IServiceProvider, TaskFlowOptions> _configureOptions;

        public DelegateNamedConfigureTaskFlowOptions(IServiceProvider serviceProvider, string name, Func<IServiceProvider, TaskFlowOptions> configureOptions)
        {
            _serviceProvider = serviceProvider;
            _configureOptions = configureOptions;
            Name = name;
        }

        public string Name { get; }

        public TaskFlowOptions Configure()
        {
            return _configureOptions(_serviceProvider);
        }
    }
}