namespace System.Threading.Tasks.Flow;

/// <summary>Separates a selected registration's admission decision from caller-facing operation completion.</summary>
public class TaskSchedulerSubmission
{
    internal TaskSchedulerSubmission(Task<TaskSchedulerAdmissionResult> admission, Task completion)
    {
        Admission = admission;
        Completion = completion;
    }

    /// <summary>Gets the admission decision. Cancellation or upstream failure before admission settles this task accordingly.</summary>
    public Task<TaskSchedulerAdmissionResult> Admission { get; }
    /// <summary>Gets caller-facing completion, which can precede underlying completion when middleware times out.</summary>
    public Task Completion { get; }
}

/// <summary>Separates admission from the eventual caller-facing result.</summary>
public sealed class TaskSchedulerSubmission<T> : TaskSchedulerSubmission
{
    internal TaskSchedulerSubmission(Task<TaskSchedulerAdmissionResult> admission, Task<T> completion) : base(admission, completion)
        => Completion = completion;
    /// <summary>Gets the caller-facing result task. Rejections fault with AdmissionRejectedException.</summary>
    public new Task<T> Completion { get; }
}