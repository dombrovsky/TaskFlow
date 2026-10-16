namespace TaskFlow.Tests.Extensions
{
    using System.Reflection;
    using System.Runtime.Versioning;
    using System.Threading.Tasks.Flow;
    using NUnit.Framework;

    [TestFixture]
    internal sealed class PackageTargetFixture
    {
        [Test]
        public void Tests_ShouldExerciseNetstandardAssets()
        {
            Assert.That(typeof(TaskFlow).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName,
                Is.EqualTo(".NETStandard,Version=v2.0"));
            Assert.That(typeof(TimeProviderTimeoutTaskSchedulerExtensions).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName,
                Is.EqualTo(".NETStandard,Version=v2.0"));
        }
    }
}
