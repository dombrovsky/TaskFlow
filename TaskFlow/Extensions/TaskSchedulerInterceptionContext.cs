namespace System.Threading.Tasks.Flow
{
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;

    /// <summary>Describes an operation observed by a task scheduler interceptor.</summary>
    [SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Lifecycle contexts have no value equality semantics.")]
    public readonly struct TaskSchedulerInterceptionContext
    {
        private readonly ExtendedState? _extendedState;

        internal TaskSchedulerInterceptionContext(object? state, CancellationToken cancellationToken)
        {
            _extendedState = state as ExtendedState;
            State = UnwrapState(state);
            CancellationToken = cancellationToken;
        }

        /// <summary>Gets the original state supplied by the caller.</summary>
        public object? State { get; }

        /// <summary>Gets the cancellation token supplied to the scheduler.</summary>
        public CancellationToken CancellationToken { get; }

        /// <summary>Gets the first operation annotation of the requested type, if present.</summary>
        public TAnnotation? GetAnnotation<TAnnotation>()
            where TAnnotation : class, IOperationAnnotation
        {
            return GetAnnotation<TAnnotation>(_extendedState);
        }

        /// <summary>Gets the first operation annotation of the requested type from scheduler state, if present.</summary>
        public static TAnnotation? GetAnnotation<TAnnotation>(object? state)
            where TAnnotation : class, IOperationAnnotation
        {
            return (state as ExtendedState).Unwrap<TAnnotation>().FirstOrDefault();
        }

        private static object? UnwrapState(object? state)
        {
            while (state is ExtendedState extendedState)
            {
                state = extendedState.State;
            }

            return state;
        }
    }
}
