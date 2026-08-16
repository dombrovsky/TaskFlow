namespace System.Threading.Tasks.Flow
{
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;

    /// <summary>Provides state, annotations, and cancellation information for an intercepted operation.</summary>
    /// <remarks>
    /// This is a lightweight value type. It does not define value equality and should be treated as lifecycle data
    /// passed to interceptor callbacks rather than as an operation identity.
    /// </remarks>
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

        /// <summary>Gets the original operation state after TaskFlow extended-state wrappers have been removed.</summary>
        /// <value>The caller-provided state, or <c>null</c> when no state was supplied.</value>
        public object? State { get; }

        /// <summary>Gets the cancellation token received by the interception decorator.</summary>
        /// <value>The token associated with the intercepted enqueue request.</value>
        public CancellationToken CancellationToken { get; }

        /// <summary>Gets the first annotation of the requested type attached to the operation.</summary>
        /// <typeparam name="TAnnotation">The reference type of annotation to retrieve.</typeparam>
        /// <returns>The first matching annotation, or <c>null</c> when the operation has no matching annotation.</returns>
        /// <remarks>Nested extended-state wrappers are searched from the outermost wrapper inward.</remarks>
        public TAnnotation? GetAnnotation<TAnnotation>()
            where TAnnotation : class, IOperationAnnotation
        {
            return GetAnnotation<TAnnotation>(_extendedState);
        }

        /// <summary>Gets the first annotation of the requested type from raw scheduler state.</summary>
        /// <typeparam name="TAnnotation">The reference type of annotation to retrieve.</typeparam>
        /// <param name="state">Raw state passed through an <see cref="ITaskScheduler"/> decorator pipeline.</param>
        /// <returns>The first matching annotation, or <c>null</c> when <paramref name="state"/> contains no matching annotation.</returns>
        /// <remarks>
        /// This helper is useful to decorators that must inspect annotations before an interception context is created.
        /// Nested extended-state wrappers are searched from the outermost wrapper inward.
        /// </remarks>
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
