namespace System.Threading.Tasks.Flow
{
    using System.Threading.Tasks.Flow.Annotations;

    /// <summary>Configures flow-owned, registration-local bounded admission.</summary>
    public static class BoundedAdmissionTaskSchedulerExtensions
    {
        /// <summary>Creates a bounded registration with captured options.</summary>
        public static ITaskScheduler WithBoundedAdmission(this ITaskScheduler scheduler, BoundedAdmissionOptions options)
            => scheduler.WithBoundedAdmission(new BoundedAdmissionController(options));

        /// <summary>Creates a bounded registration identified by a controller.</summary>
        public static ITaskScheduler WithBoundedAdmission(this ITaskScheduler scheduler, BoundedAdmissionController admission)
        {
            Argument.NotNull(scheduler);
            Argument.NotNull(admission);
            return scheduler.UseMiddleware(admission);
        }

        /// <summary>Creates a bounded registration with an explicitly owned lifetime.</summary>
        public static ITaskScheduler WithBoundedAdmission(this ITaskScheduler scheduler, BoundedAdmissionController admission, TaskSchedulerMiddlewareLifetime lifetime)
        {
            Argument.NotNull(scheduler);
            Argument.NotNull(admission);
            return scheduler.UseMiddleware(admission, lifetime);
        }
    }
}
