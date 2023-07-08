namespace System.Threading.Tasks.Flow
{
    public sealed class DedicatedThreadTaskFlow : ThreadTaskFlow
    {
        private readonly Thread _thread;

        public DedicatedThreadTaskFlow(string? name = default)
            : this(TaskFlowOptions.Default, name)
        {
        }

        public DedicatedThreadTaskFlow(TaskFlowOptions options, string? name = default)
            : base(options)
        {
            _thread = new Thread(ThreadStart)
            {
                Name = string.IsNullOrEmpty(name) ? nameof(DedicatedThreadTaskFlow) : name,
                IsBackground = true,
            };

            Starting();
            _thread.Start(null);
        }

        public override int ThreadId => _thread.ManagedThreadId;
    }
}