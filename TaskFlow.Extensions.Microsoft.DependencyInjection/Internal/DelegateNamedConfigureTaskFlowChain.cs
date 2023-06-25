namespace System.Threading.Tasks.Flow.Internal
{
    using System.Threading.Tasks.Flow.Annotations;

    internal sealed class DelegateNamedConfigureTaskFlowChain : INamedConfigureTaskFlowChain
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly Func<ITaskScheduler, IServiceProvider, ITaskScheduler> _configureSchedulerChain;

        public DelegateNamedConfigureTaskFlowChain(IServiceProvider serviceProvider, string name, Func<ITaskScheduler, IServiceProvider, ITaskScheduler> configureSchedulerChain)
        {
            _serviceProvider = serviceProvider;
            _configureSchedulerChain = configureSchedulerChain;
            Name = name;
        }

        public string Name { get; }

        public ITaskScheduler ConfigureChain(ITaskScheduler taskScheduler)
        {
            Argument.NotNull(taskScheduler);

            return _configureSchedulerChain(taskScheduler, _serviceProvider);
        }
    }
}