namespace TaskFlow.Tests.Extensions
{
    using NUnit.Framework;
    using System.Threading.Tasks.Flow;

    [TestFixture]
    internal sealed class WrapperOwnershipTaskSchedulerExtensionsFixture
    {
        private ITaskFlow? _taskFlow;

        [TearDown]
        public void TearDown()
        {
            _taskFlow?.Dispose(TimeSpan.FromSeconds(1));
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public void ExtensionWrappers_ShouldNotOwnTaskFlow_DisposableInterfacesAreNotExposed(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;

            var timeout = taskFlow.WithTimeout(TimeSpan.FromSeconds(1));
            var throttle = taskFlow.WithThrottle(TimeSpan.FromSeconds(1));
            var cancelPrevious = taskFlow.CreateCancelPrevious();
            var cancellationScope = taskFlow.CreateCancellationScope(CancellationToken.None);
            var intercepted = taskFlow.Intercept(new NoOpAsyncInterceptorFactory());
            var annotated = taskFlow.WithOperationName("non-owning");
            var middleware = taskFlow.UseMiddleware(new NoOpMiddleware());

            Assert.That(timeout, Is.Not.InstanceOf<IDisposable>());
            Assert.That(timeout, Is.Not.InstanceOf<IAsyncDisposable>());

            Assert.That(throttle, Is.Not.InstanceOf<IDisposable>());
            Assert.That(throttle, Is.Not.InstanceOf<IAsyncDisposable>());

            Assert.That(cancelPrevious, Is.Not.InstanceOf<IDisposable>());
            Assert.That(cancelPrevious, Is.Not.InstanceOf<IAsyncDisposable>());

            Assert.That(cancellationScope, Is.Not.InstanceOf<IDisposable>());
            Assert.That(cancellationScope, Is.Not.InstanceOf<IAsyncDisposable>());

            Assert.That(intercepted, Is.Not.InstanceOf<IDisposable>());
            Assert.That(intercepted, Is.Not.InstanceOf<IAsyncDisposable>());

            Assert.That(annotated, Is.Not.InstanceOf<IDisposable>());
            Assert.That(annotated, Is.Not.InstanceOf<IAsyncDisposable>());

            Assert.That(middleware, Is.Not.InstanceOf<IDisposable>());
            Assert.That(middleware, Is.Not.InstanceOf<IAsyncDisposable>());
        }

        private sealed class NoOpAsyncInterceptorFactory : IAsyncTaskSchedulerInterceptor
        {
            public IAsyncTaskInterceptor CreateInterceptor(TaskSchedulerInterceptionContext context)
            {
                return new NoOpAsyncTaskInterceptor();
            }
        }

        private sealed class NoOpAsyncTaskInterceptor : IAsyncTaskInterceptor
        {
            public ValueTask OnBeforeAsync(TaskSchedulerInterceptionContext context)
            {
                return default;
            }

            public ValueTask OnFinallyAsync(TaskSchedulerInterceptionContext context)
            {
                return default;
            }

            public ValueTask OnErrorAsync(TaskSchedulerInterceptionContext context, Exception exception)
            {
                return default;
            }

            public ValueTask OnSuccessAsync<TResult>(TaskSchedulerInterceptionContext context, TResult result)
            {
                return default;
            }
        }

        private sealed class NoOpMiddleware : ITaskSchedulerExecutionMiddleware
        {
            public ValueTask<TResult> InvokeAsync<TResult>(TaskSchedulerOperationContext context, TaskSchedulerExecutionDelegate<TResult> continuation)
            {
                return continuation(context);
            }
        }
    }
}
