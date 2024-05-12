namespace System.Threading.Tasks.Flow
{
    internal sealed class ExtendedState
    {
        public ExtendedState(object? state, object? extended)
        {
            State = state;
            Extended = extended;
        }

        public object? State { get; }

        public object? Extended { get; }
    }
}