namespace System.Threading.Tasks.Flow
{
    public sealed class CurrentThreadTaskFlow : ThreadTaskFlow
    {
        private int _managedThreadId;

        public CurrentThreadTaskFlow()
            : this(TaskFlowOptions.Default)
        {
        }

        public CurrentThreadTaskFlow(TaskFlowOptions options)
            : base(options)
        {
        }

        public override int ThreadId => _managedThreadId;

        public void Run()
        {
            Starting();
            _managedThreadId = Environment.CurrentManagedThreadId;
            ThreadStart(null);
        }
    }
}