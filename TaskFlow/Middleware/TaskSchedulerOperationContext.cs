namespace System.Threading.Tasks.Flow
{
    using System;
    using System.Threading;

    /// <summary>Provides immutable operation facts and registration-scoped metadata to execution and completion middleware.</summary>
    /// <remarks>
    /// <para>The context belongs to one scheduled operation. For middleware, it is also associated with one registration and therefore one captured
    /// annotation scope and one local-state slot.</para>
    /// <para>The final context used by annotation-aware operation invocation is not associated with a middleware registration and cannot create local
    /// state.</para>
    /// </remarks>
    public sealed class TaskSchedulerOperationContext
    {
        private readonly Internal.PipelineOperation _operation;
        private readonly Internal.AnnotationScope? _annotations;
        private readonly int _registrationIndex;
        private readonly ITaskScheduler? _scheduler;

        internal TaskSchedulerOperationContext(
            Internal.PipelineOperation operation,
            Internal.AnnotationScope? annotations,
            int registrationIndex,
            CancellationToken cancellationToken,
            ITaskScheduler? scheduler = null)
        {
            _operation = operation;
            _annotations = annotations;
            _registrationIndex = registrationIndex;
            _scheduler = scheduler;
            CancellationToken = cancellationToken;
        }

        /// <summary>Gets the unmodified state supplied to <see cref="ITaskScheduler.Enqueue{TResult}"/>.</summary>
        /// <value>The original state reference, which may be <c>null</c>.</value>
        public object? State => _operation.State;

        /// <summary>Gets the effective producer cancellation token captured before terminal scheduling.</summary>
        /// <value>The token selected by the enqueue pipeline and supplied to the terminal scheduler.</value>
        /// <remarks>This value can differ from <see cref="CallerCancellationToken"/> when enqueue middleware separates caller waiting from producer work.</remarks>
        public CancellationToken CancellationToken { get; }

        /// <summary>Gets the submitting caller's original cancellation token.</summary>
        /// <value>The token supplied when the operation was enqueued, independently of producer-token transformations.</value>
        public CancellationToken CallerCancellationToken => _operation.CallerCancellationToken;

        /// <summary>Gets the annotation visible when this middleware registration was created.</summary>
        /// <typeparam name="TAnnotation">The annotation type.</typeparam>
        /// <returns>The nearest visible annotation registered under exactly <typeparamref name="TAnnotation"/>, or <c>null</c>.</returns>
        /// <remarks>A middleware context sees the annotation scope captured when that middleware was registered. The final operation context sees the
        /// pipeline's final scope. Lookup is by the exact registered type rather than assignability.</remarks>
        public TAnnotation? GetAnnotation<TAnnotation>() where TAnnotation : class, IOperationAnnotation
            => _annotations?.Get<TAnnotation>();

        /// <summary>Gets this middleware registration's local state for the current operation.</summary>
        /// <typeparam name="TState">The state type.</typeparam>
        /// <returns>The existing state when initialized with <typeparamref name="TState"/>; otherwise, <c>null</c>.</returns>
        /// <exception cref="InvalidOperationException">The slot was initialized with a different state type.</exception>
        /// <remarks>Returns <c>null</c> for a final operation context because it has no middleware registration slot.</remarks>
        public TState? GetLocalState<TState>() where TState : class
            => _registrationIndex < 0 ? null : _operation.GetLocalState<TState>(_registrationIndex);

        /// <summary>Gets or atomically creates this middleware registration's local state for the current operation.</summary>
        /// <typeparam name="TState">The reference-type state container.</typeparam>
        /// <param name="stateFactory">Creates the state when this registration has not initialized it.</param>
        /// <returns>The existing state, or the non-null state created by <paramref name="stateFactory"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="stateFactory"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The context has no middleware registration, the factory returns <c>null</c>, or the slot was
        /// initialized with a different state type.</exception>
        /// <remarks>All phases belonging to one compound <see cref="TaskSchedulerMiddlewareExtensions.UseMiddleware"/> registration share this slot.
        /// Slots remain isolated across operations and registrations.</remarks>
        public TState GetOrCreateLocalState<TState>(Func<TState> stateFactory) where TState : class
        {
            Annotations.Argument.NotNull(stateFactory);
            if (_registrationIndex < 0) throw new InvalidOperationException("The final operation context has no middleware registration-local state.");
            return _operation.GetOrCreateLocalState(_registrationIndex, stateFactory);
        }

        internal Internal.PipelineOperation Operation => _operation;
        internal ITaskScheduler Scheduler => _scheduler ?? throw new InvalidOperationException("The context is not associated with a middleware registration.");
        internal IOperationAnnotation? GetAnnotation(Type type) => _annotations?.Get(type);
    }
}
