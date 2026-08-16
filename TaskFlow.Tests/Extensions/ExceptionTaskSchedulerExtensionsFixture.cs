namespace TaskFlow.Tests.Extensions
{
    using NUnit.Framework;
    using System.Threading.Tasks.Flow;

    [TestFixture]
    internal sealed class ExceptionTaskSchedulerExtensionsFixture
    {
        private static readonly string[] FooAndGeneric = ["foo", "generic"];
        private static readonly string[] BarAndGeneric = ["bar", "generic"];
        private static readonly string[] Generic = ["generic"];
        private ITaskFlow? _taskFlow;

        [TearDown]
        public void TearDown()
        {
            _taskFlow?.Dispose(TimeSpan.FromSeconds(1));
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public void Enqueue_ShouldExecuteHandler_IfExceptionOfThatTypeOccurred(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;

            var exceptions = new List<Exception>();
            var task = taskFlow
                .OnError<InvalidOperationException>(exceptions.Add)
                .Enqueue(() => throw new InvalidOperationException());

            Assert.That(async () => await task.ConfigureAwait(false), Throws.InstanceOf<InvalidOperationException>());
            Assert.That(exceptions, Has.One.TypeOf<InvalidOperationException>());
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public void Enqueue_ShouldNotExecuteHandler_IfExceptionOfAnotherTypeOccurred(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;

            var exceptions = new List<Exception>();
            var task = taskFlow
                .OnError<InvalidOperationException>(exceptions.Add)
                .Enqueue(() => throw new ArgumentException("Expected test failure"));

            Assert.That(async () => await task.ConfigureAwait(false), Throws.InstanceOf<ArgumentException>());
            Assert.That(exceptions, Is.Empty);
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public void Enqueue_ShouldExecuteHandler_IfExceptionMatchingFilter(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;

            var exceptions = new List<Exception>();
            var task = taskFlow
                .OnError<InvalidOperationException>(exceptions.Add, exception => exception.Message.Contains("foo", StringComparison.Ordinal))
                .Enqueue(() => throw new InvalidOperationException("foo"));

            Assert.That(async () => await task.ConfigureAwait(false), Throws.InstanceOf<InvalidOperationException>());
            Assert.That(exceptions, Has.One.TypeOf<InvalidOperationException>());
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public void Enqueue_ShouldNotExecuteHandler_IfExceptionNotMatchingFilter(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;

            var exceptions = new List<Exception>();
            var task = taskFlow
                .OnError<InvalidOperationException>(exceptions.Add, exception => exception.Message.Contains("bar", StringComparison.Ordinal))
                .Enqueue(() => throw new InvalidOperationException("foo"));

            Assert.That(async () => await task.ConfigureAwait(false), Throws.InstanceOf<InvalidOperationException>());
            Assert.That(exceptions, Is.Empty);
        }

        [TestCaseSource(typeof(TaskFlows), nameof(TaskFlows.CreateTaskFlows))]
        public void Enqueue_MultipleHandlers_ShouldExecuteAllMatchingHandlers(ITaskFlow taskFlow)
        {
            _taskFlow = taskFlow;

            var exceptions = new List<string>();
            var errorHandlingScheduler = taskFlow
                .OnError<InvalidOperationException>(_ => exceptions.Add("foo"), exception => exception.Message.Contains("foo", StringComparison.Ordinal))
                .OnError<InvalidOperationException>(_ => exceptions.Add("bar"), exception => exception.Message.Contains("bar", StringComparison.Ordinal))
                .OnError(_ => exceptions.Add("generic"));

            var task1 = errorHandlingScheduler.Enqueue(() => throw new InvalidOperationException("foo"));

            Assert.That(async () => await task1.ConfigureAwait(false), Throws.InstanceOf<InvalidOperationException>());
            Assert.That(exceptions, Is.EqualTo(FooAndGeneric));
            exceptions.Clear();

            var task2 = errorHandlingScheduler.Enqueue(() => throw new InvalidOperationException("bar"));
            Assert.That(async () => await task2.ConfigureAwait(false), Throws.InstanceOf<InvalidOperationException>());
            Assert.That(exceptions, Is.EqualTo(BarAndGeneric));
            exceptions.Clear();

            var task3 = errorHandlingScheduler.Enqueue(() => throw new InvalidOperationException());
            Assert.That(async () => await task3.ConfigureAwait(false), Throws.InstanceOf<InvalidOperationException>());
            Assert.That(exceptions, Is.EqualTo(Generic));
            exceptions.Clear();

            var task4 = errorHandlingScheduler.Enqueue(() => throw new ArgumentException("Expected test failure"));
            Assert.That(async () => await task4.ConfigureAwait(false), Throws.InstanceOf<ArgumentException>());
            Assert.That(exceptions, Is.EqualTo(Generic));
            exceptions.Clear();
        }
    }
}
