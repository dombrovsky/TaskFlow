namespace System.Threading.Tasks.Flow.Internal
{
    internal sealed class MiddlewareRegistration
    {
        public MiddlewareRegistration(object middleware, AnnotationScope? annotations)
        {
            Middleware = middleware;
            Annotations = annotations;
        }

        public object Middleware { get; }
        public AnnotationScope? Annotations { get; }
        public ITaskScheduler Scheduler { get; set; } = null!;
    }
}
