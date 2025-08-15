namespace System.Threading.Tasks.Flow
{
    using System.Runtime.Serialization;

    /// <summary>
    /// Represents an exception that is thrown when an operation is throttled or rejected due to timing constraints.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="OperationThrottledException"/> is thrown by throttling mechanisms in the TaskFlow system
    /// when operations are rejected based on timing or rate limiting policies. This exception derives from
    /// <see cref="OperationCanceledException"/> because throttled operations are effectively cancelled due
    /// to policy constraints rather than explicit user cancellation.
    /// </para>
    /// <para>
    /// This exception is commonly thrown by:
    /// </para>
    /// <list type="bullet">
    ///   <item>Debounce mechanisms when operations are enqueued too quickly</item>
    ///   <item>Rate limiting systems when operation frequency exceeds allowed thresholds</item>
    ///   <item>Throttling wrappers that enforce minimum intervals between operations</item>
    ///   <item>Backpressure systems that reject operations under high load</item>
    /// </list>
    /// <para>
    /// Unlike regular <see cref="OperationCanceledException"/>, this exception specifically indicates that
    /// the operation was cancelled due to throttling policies rather than explicit cancellation requests.
    /// This distinction allows applications to handle throttling scenarios differently from user-initiated
    /// cancellations.
    /// </para>
    /// <para>
    /// The exception is serializable and supports the standard .NET exception patterns for proper
    /// cross-AppDomain and serialization scenarios.
    /// </para>
    /// </remarks>
    /// <example>
    /// <para>Handling throttled operations in a debounce scenario:</para>
    /// <code>
    /// var debouncedScheduler = scheduler.WithDebounce(TimeSpan.FromSeconds(1));
    /// 
    /// try 
    /// {
    ///     // These operations are enqueued too quickly
    ///     await debouncedScheduler.Enqueue(() => SaveDocumentAsync());
    ///     await debouncedScheduler.Enqueue(() => SaveDocumentAsync()); // Too soon - will throw
    /// }
    /// catch (OperationThrottledException ex)
    /// {
    ///     // Handle throttling - maybe show user feedback or retry later
    ///     _logger.LogInformation("Save operation throttled: {Message}", ex.Message);
    ///     
    ///     // Could implement retry logic
    ///     await Task.Delay(TimeSpan.FromSeconds(1));
    ///     await debouncedScheduler.Enqueue(() => SaveDocumentAsync());
    /// }
    /// catch (OperationCanceledException ex) when (ex is not OperationThrottledException)
    /// {
    ///     // Handle explicit cancellation differently
    ///     _logger.LogInformation("Save operation was cancelled by user");
    /// }
    /// </code>
    /// <para>Differentiating throttling from other cancellations:</para>
    /// <code>
    /// try 
    /// {
    ///     await throttledScheduler.Enqueue(() => SomeOperationAsync());
    /// }
    /// catch (OperationThrottledException)
    /// {
    ///     // Specific handling for throttling
    ///     ShowUserMessage("Operation rate limited. Please wait and try again.");
    /// }
    /// catch (OperationCanceledException)
    /// {
    ///     // Handle other cancellation scenarios
    ///     ShowUserMessage("Operation was cancelled.");
    /// }
    /// </code>
    /// </example>
    [Serializable]
    public sealed class OperationThrottledException : OperationCanceledException
    {
        public OperationThrottledException()
        {
        }

        public OperationThrottledException(string message)
            : base(message)
        {
        }

        public OperationThrottledException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

#if !NET8_0_OR_GREATER
        private OperationThrottledException(SerializationInfo serializationInfo, StreamingContext streamingContext)
            : base(serializationInfo, streamingContext)
        {
        }
#endif
    }
}