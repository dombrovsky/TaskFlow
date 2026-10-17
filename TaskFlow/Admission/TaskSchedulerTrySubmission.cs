namespace System.Threading.Tasks.Flow;

/// <summary>A synchronous admission decision and, on acceptance, observable operation completion.</summary>
/// <remarks>Rejected submissions have no completion task. Their internal pipeline failures are observed by the scheduler.</remarks>
public class TaskSchedulerTrySubmission
{
    internal TaskSchedulerTrySubmission(TaskSchedulerAdmissionResult admission, Task? completion)
    {
        Admission = admission;
        Completion = completion;
    }

    /// <summary>Gets the immediate combined admission decision: Accepted, Full, or Closed.</summary>
    public TaskSchedulerAdmissionResult Admission { get; }

    /// <summary>Gets caller-facing completion on acceptance, or null on rejection. Observe accepted tasks for cancellation and failures.</summary>
    public Task? Completion { get; }
}

/// <summary>A synchronous admission decision and, on acceptance, an observable result task.</summary>
public sealed class TaskSchedulerTrySubmission<T> : TaskSchedulerTrySubmission
{
    internal TaskSchedulerTrySubmission(TaskSchedulerAdmissionResult admission, Task<T>? completion) : base(admission, completion)
        => Completion = completion;

    /// <summary>Gets the reusable result task on acceptance, or null on rejection.</summary>
    public new Task<T>? Completion { get; }
}