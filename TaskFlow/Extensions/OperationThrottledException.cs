namespace System.Threading.Tasks.Flow
{
    using System.Runtime.Serialization;

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