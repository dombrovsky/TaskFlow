namespace System.Threading.Tasks.Flow
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Runtime.ExceptionServices;

    /// <summary>Represents the current successful result or captured failure of a scheduled operation.</summary>
    /// <typeparam name="TResult">The successful result type.</typeparam>
    /// <remarks>
    /// <para>Completion middleware receives and returns this value to observe or transform completion without throwing merely to represent failure.</para>
    /// <para>A failure captures <see cref="ExceptionDispatchInfo"/> when it is created. Passing that outcome onward unchanged preserves exception identity
    /// and its captured stack when the pipeline ultimately rethrows it.</para>
    /// <para>The default value represents a successful outcome whose <see cref="Result"/> is the default value of <typeparamref name="TResult"/>. Prefer
    /// the named factories when intentionally creating an outcome.</para>
    /// </remarks>
    [SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Named factories avoid ambiguous constructors when TResult is Exception.")]
    [SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "Operation outcomes have no value equality semantics.")]
    public readonly struct TaskSchedulerOperationOutcome<TResult>
    {
        private readonly TResult _result;
        private readonly ExceptionDispatchInfo? _exception;

        private TaskSchedulerOperationOutcome(TResult result)
        {
            _result = result;
            _exception = null;
        }

        private TaskSchedulerOperationOutcome(Exception exception)
        {
            _result = default!;
            Annotations.Argument.NotNull(exception);
            _exception = ExceptionDispatchInfo.Capture(exception);
        }

        /// <summary>Gets a value indicating whether the outcome contains a successful result.</summary>
        /// <value><c>true</c> when no exception is captured; otherwise, <c>false</c>.</value>
        public bool IsSuccess => _exception == null;

        /// <summary>Gets the successful operation result.</summary>
        /// <value>The result supplied to <see cref="FromResult"/>.</value>
        /// <exception cref="InvalidOperationException">The outcome represents a failure.</exception>
        public TResult Result => IsSuccess
            ? _result
            : throw new InvalidOperationException("A failed operation outcome does not have a result.");

        /// <summary>Gets the original exception represented by a failed outcome.</summary>
        /// <value>The captured exception instance, or <c>null</c> when <see cref="IsSuccess"/> is <c>true</c>.</value>
        /// <remarks>Reading this property does not throw the exception or alter its captured dispatch information.</remarks>
        public Exception? Exception => _exception?.SourceException;

        /// <summary>Creates an outcome representing successful operation completion.</summary>
        /// <param name="result">The operation result.</param>
        /// <returns>A successful outcome containing <paramref name="result"/>. The result may be <c>null</c> when permitted by
        /// <typeparamref name="TResult"/>.</returns>
        public static TaskSchedulerOperationOutcome<TResult> FromResult(TResult result) => new TaskSchedulerOperationOutcome<TResult>(result);

        /// <summary>Creates a failed outcome and captures the exception's dispatch information.</summary>
        /// <param name="exception">The operation or middleware exception.</param>
        /// <returns>A failed outcome containing the same exception instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <c>null</c>.</exception>
        /// <remarks>Create a new failure only when intentionally introducing or replacing a failure. Forward an existing outcome unchanged to preserve
        /// the dispatch information it already contains.</remarks>
        public static TaskSchedulerOperationOutcome<TResult> FromException(Exception exception) => new TaskSchedulerOperationOutcome<TResult>(exception);

        internal TResult GetResultOrThrow()
        {
            _exception?.Throw();
            return _result;
        }
    }
}
