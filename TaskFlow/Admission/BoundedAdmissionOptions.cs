namespace System.Threading.Tasks.Flow;

/// <summary>Defines which accepted operations consume capacity.</summary>
public enum TaskSchedulerCapacityMode
{
    /// <summary>Counts accepted operations until underlying work settles, including running operations.</summary>
    Outstanding,
    /// <summary>Counts accepted operations until their user delegate first starts.</summary>
    Pending
}

/// <summary>Defines behavior when admission capacity is unavailable.</summary>
public enum TaskSchedulerAdmissionMode
{
    /// <summary>Wait asynchronously in FIFO order.</summary>
    Wait,
    /// <summary>Reject without waiting for capacity when the registration is reached.</summary>
    Reject
}

/// <summary>Describes admission independently of eventual operation completion.</summary>
public enum TaskSchedulerAdmissionResult
{
    /// <summary>The selected registration reserved capacity.</summary>
    Accepted,
    /// <summary>Immediate admission found no available capacity.</summary>
    Full,
    /// <summary>The configured admission waiter limit was reached.</summary>
    WaiterLimitReached,
    /// <summary>The registration's lifetime is stopping.</summary>
    Closed
}

/// <summary>Configures one bounded registration. Values are captured when its controller is constructed.</summary>
public sealed class BoundedAdmissionOptions
{
    /// <summary>Gets or sets a positive capacity.</summary>
    public int Capacity { get; set; }
    /// <summary>Gets or sets the accounting mode. The default is outstanding operations.</summary>
    public TaskSchedulerCapacityMode CapacityMode { get; set; }
    /// <summary>Gets or sets the default admission mode used by Enqueue. The default is asynchronous waiting.</summary>
    public TaskSchedulerAdmissionMode AdmissionMode { get; set; }
    /// <summary>Gets or sets the nonnegative waiter limit, or null for unlimited waiters.</summary>
    public int? MaxAdmissionWaiters { get; set; }
}