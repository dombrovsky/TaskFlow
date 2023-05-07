namespace System.Threading.Tasks.Flow
{
    using System;

    public partial class TaskFlow
    {
        public static TaskScheduler TaskScheduler { get; set; } = TaskScheduler.Default;

        public static TimeSpan DisposeTimeout { get; set; } = Timeout.InfiniteTimeSpan;
    }
}
