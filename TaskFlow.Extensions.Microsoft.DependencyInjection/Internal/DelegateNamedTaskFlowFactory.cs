namespace System.Threading.Tasks.Flow.Internal
{
    using System.Threading.Tasks.Flow.Annotations;

    internal sealed class DelegateNamedTaskFlowFactory : INamedTaskFlowFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly Func<IServiceProvider, TaskFlowOptions, ITaskFlow> _taskFlowFactory;

        public DelegateNamedTaskFlowFactory(IServiceProvider serviceProvider, string name, Func<IServiceProvider, TaskFlowOptions, ITaskFlow> taskFlowFactory)
        {
            Name = name;
            _serviceProvider = serviceProvider;
            _taskFlowFactory = taskFlowFactory;
        }

        public string Name { get; }

        public ITaskFlow Create(TaskFlowOptions options)
        {
            Argument.NotNull(options);

            return _taskFlowFactory(_serviceProvider, options);
        }
    }
}