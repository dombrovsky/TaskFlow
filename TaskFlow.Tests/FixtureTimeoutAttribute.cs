namespace TaskFlow.Tests
{
    using NUnit.Framework;

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = false, Inherited = true)]
    public class FixtureTimeoutAttribute : TimeoutAttribute
    {
        public FixtureTimeoutAttribute(int timeout) : base(timeout)
        {
        }
    }
}