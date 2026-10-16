namespace System.Threading.Tasks.Flow
{
    /// <summary>Indicates rejection before the selected operation was admitted.</summary>
    public sealed class AdmissionRejectedException : InvalidOperationException
    {
        /// <summary>Creates a capacity rejection.</summary>
        public AdmissionRejectedException() : this(TaskSchedulerAdmissionResult.Full) { }
        /// <summary>Creates a capacity rejection with a message.</summary>
        public AdmissionRejectedException(string message) : base(message) { }
        /// <summary>Creates a capacity rejection with a message and cause.</summary>
        public AdmissionRejectedException(string message, Exception innerException) : base(message, innerException) { }
        /// <summary>Creates a rejection with its admission result.</summary>
        public AdmissionRejectedException(TaskSchedulerAdmissionResult result) : base($"Operation admission rejected: {result}.")
        {
            if (result < TaskSchedulerAdmissionResult.Full || result > TaskSchedulerAdmissionResult.Closed)
                throw new ArgumentOutOfRangeException(nameof(result));
            Result = result;
        }
        /// <summary>Gets the rejection reason.</summary>
        public TaskSchedulerAdmissionResult Result { get; } = TaskSchedulerAdmissionResult.Full;
    }
}
