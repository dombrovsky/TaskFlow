namespace System.Threading.Tasks.Flow;

using System;
using System.Threading;

/// <summary>Provides immutable enqueue facts, cancellation, metadata, and local state to enqueue middleware.</summary>
/// <typeparam name="TResult">The scheduled operation result type.</typeparam>
/// <remarks>
/// <para>Each context is associated with one scheduled operation and one middleware registration. Context instances are immutable; changing the
/// producer cancellation token creates a copy for the same operation.</para>
/// <para>The context distinguishes the caller's original token from the effective producer token flowing toward the terminal scheduler. It also
/// exposes only annotations that existed when the current middleware was registered.</para>
/// </remarks>
public sealed class TaskSchedulerEnqueueContext<TResult>
{
    private readonly Internal.PipelineOperation<TResult> _operation;
    private readonly Internal.AnnotationScope? _annotations;
    private readonly int _registrationIndex;

    internal TaskSchedulerEnqueueContext(
        Internal.PipelineOperation<TResult> operation,
        Internal.AnnotationScope? annotations,
        int registrationIndex,
        CancellationToken cancellationToken)
    {
        _operation = operation;
        _annotations = annotations;
        _registrationIndex = registrationIndex;
        CancellationToken = cancellationToken;
    }

    /// <summary>Gets the unmodified state supplied to <see cref="ITaskScheduler.Enqueue{TResult}"/>.</summary>
    /// <value>The original state reference, which may be <c>null</c>.</value>
    /// <remarks>Middleware transport does not wrap or replace this value, and the same reference is forwarded to the terminal scheduler.</remarks>
    public object? State => _operation.State;

    /// <summary>Gets the cancellation token supplied by the submitting caller.</summary>
    /// <value>The original caller token, even when enqueue middleware changes <see cref="CancellationToken"/>.</value>
    /// <remarks>Use this token for caller-wait behavior that must remain independent of shared or transformed producer work.</remarks>
    public CancellationToken CallerCancellationToken => _operation.CallerCancellationToken;

    /// <summary>Gets the effective producer cancellation token for the next enqueue phase.</summary>
    /// <value>The token currently flowing toward the terminal scheduler.</value>
    /// <remarks>The initial value is the caller token. Earlier enqueue middleware may replace it by passing a context returned by
    /// <see cref="WithCancellationToken"/> to its continuation.</remarks>
    public CancellationToken CancellationToken { get; }

    /// <summary>Gets the annotation visible when this middleware registration was created.</summary>
    /// <typeparam name="TAnnotation">The annotation type.</typeparam>
    /// <returns>The nearest visible annotation registered under exactly <typeparamref name="TAnnotation"/>, or <c>null</c> when no such annotation
    /// was visible at registration time.</returns>
    /// <remarks>Annotations are forward-scoped. Values added after this middleware registration are not visible. A later value registered under the
    /// same type shadows an earlier value only for later registrations and the final operation.</remarks>
    public TAnnotation? GetAnnotation<TAnnotation>() where TAnnotation : class, IOperationAnnotation
        => _annotations?.Get<TAnnotation>();

    /// <summary>Gets this middleware registration's local state for the current operation.</summary>
    /// <typeparam name="TState">The state type.</typeparam>
    /// <returns>The existing state when this registration initialized a slot with <typeparamref name="TState"/>; otherwise, <c>null</c>.</returns>
    /// <exception cref="InvalidOperationException">The slot was initialized with a different state type.</exception>
    /// <remarks>The slot is isolated from other operations and other registrations, including another registration of the same middleware object.</remarks>
    public TState? GetLocalState<TState>() where TState : class
        => _operation.GetLocalState<TState>(_registrationIndex);

    /// <summary>Gets or atomically creates this middleware registration's local state for the current operation.</summary>
    /// <typeparam name="TState">The reference-type state container.</typeparam>
    /// <param name="stateFactory">Creates the state when it has not been initialized.</param>
    /// <returns>The existing state, or the non-null state created by <paramref name="stateFactory"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stateFactory"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">The factory returns <c>null</c>, or the slot was initialized with a different state type.</exception>
    /// <remarks>The factory is used to initialize a per-operation, per-registration slot. Compound middleware registered through
    /// <see cref="TaskSchedulerMiddlewareExtensions.UseMiddleware"/> can retrieve the same value from its execution and completion phases.</remarks>
    public TState GetOrCreateLocalState<TState>(Func<TState> stateFactory) where TState : class
    {
        Annotations.Argument.NotNull(stateFactory);
        return _operation.GetOrCreateLocalState(_registrationIndex, stateFactory);
    }

    /// <summary>Creates an enqueue context for the same operation with a different effective producer cancellation token.</summary>
    /// <param name="cancellationToken">The token to pass to the next enqueue phase and terminal scheduler.</param>
    /// <returns>A new context retaining the original state, caller token, captured annotations, operation identity, and registration-local state.</returns>
    /// <remarks>Pass the returned context to the enqueue continuation. This method does not cancel, link, or dispose either token and does not mutate
    /// the current context.</remarks>
    public TaskSchedulerEnqueueContext<TResult> WithCancellationToken(CancellationToken cancellationToken)
        => new TaskSchedulerEnqueueContext<TResult>(_operation, _annotations, _registrationIndex, cancellationToken);

    /// <summary>Separates caller waiting from operation-owned downstream work.</summary>
    /// <param name="continuation">The downstream enqueue continuation to transfer to operation ownership.</param>
    /// <param name="callerWait">Starts work through the supplied continuation and returns the caller's wait task.</param>
    /// <returns>The caller's wait task, which may settle before the underlying work.</returns>
    /// <remarks>
    /// <para>Invoke the supplied continuation at most once, before the caller-wait task settles. The operation retains its work
    /// even if the caller stops waiting or the wait policy throws after forwarding. Ordinary middleware must return or
    /// await the continuations it starts; use this boundary when a policy intentionally returns an independent caller wait.</para>
    /// <para>The supplied continuation's task follows the immediate downstream result. If a downstream policy also separates
    /// caller waiting, this task may settle before the user delegate or completion processing finishes. It is not a signal of
    /// actual producer termination. Nested boundaries retain operation ownership until all underlying work settles, so caller
    /// completion does not release outstanding admission capacity or permit owned middleware cleanup.</para>
    /// <para>The boundary preserves synchronous forwarding exceptions; an async wait policy may report them through its task.
    /// The wait policy must observe downstream failures even if all callers stop waiting, and keep its producer resources alive
    /// for as long as the work using them requires. Arbitrary detached execution/completion callbacks remain unsupported.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Either argument is <c>null</c>.</exception>
    public Task<TResult> InvokeWithCallerWait(TaskSchedulerEnqueueDelegate<TResult> continuation,
        Func<TaskSchedulerEnqueueDelegate<TResult>, Task<TResult>> callerWait)
    {
        Annotations.Argument.NotNull(continuation);
        Annotations.Argument.NotNull(callerWait);
        return callerWait(nextContext =>
        {
            _operation.AddActivity();
            Task<TResult> producer;
            try
            {
                producer = continuation(nextContext);
                Annotations.Argument.NotNull(producer);
            }
            catch
            {
                _operation.ReleaseActivity();
                throw;
            }
            return AwaitProducer(_operation, producer);
        });

        static async Task<TResult> AwaitProducer(Internal.PipelineOperation<TResult> operation, Task<TResult> producer)
        {
            try
            { return await producer.ConfigureAwait(false); }
            finally { operation.ReleaseActivity(); }
        }
    }

    internal Internal.PipelineOperation<TResult> Operation => _operation;
}