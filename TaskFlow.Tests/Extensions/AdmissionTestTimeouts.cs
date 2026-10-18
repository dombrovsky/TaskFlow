namespace TaskFlow.Tests.Extensions;

using NUnit.Framework;

// CancelAfter only signals NUnit's token; it cannot interrupt an unobserved task or wait.
internal static class AdmissionTestTimeouts
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    internal static Task WaitForTestAsync(this Task task) => task.WaitAsync(Timeout, TestContext.CurrentContext.CancellationToken);
    internal static Task<T> WaitForTestAsync<T>(this Task<T> task) => task.WaitAsync(Timeout, TestContext.CurrentContext.CancellationToken);
    internal static Task WaitForTestAsync(this ValueTask task) => task.AsTask().WaitForTestAsync();
    internal static Task<T> WaitForTestAsync<T>(this ValueTask<T> task) => task.AsTask().WaitForTestAsync();

    internal static void WaitForTest(this ManualResetEventSlim gate)
    {
        if (!gate.Wait(Timeout, TestContext.CurrentContext.CancellationToken))
            throw new TimeoutException("The test did not release its blocking gate.");
    }

}

internal sealed class AdmissionTestOwner<T>(Func<T> create) : IAsyncDisposable where T : IAsyncDisposable
{
    internal T Value { get; } = create();

    // Invoke disposal off the test thread as well as bounding its returned task. Cleanup
    // must remain bounded even if a regression blocks synchronously before returning it.
    public ValueTask DisposeAsync() => new(Task.Run(async () => await Value.DisposeAsync().ConfigureAwait(false))
        .WaitAsync(AdmissionTestTimeouts.Timeout));
}

internal sealed class AdmissionTestGate : TaskCompletionSource<int>
{
    private readonly CancellationToken _cancellation = TestContext.CurrentContext.CancellationToken;

    internal AdmissionTestGate() : base(TaskCreationOptions.RunContinuationsAsynchronously) { }
    public new Task<int> Task => base.Task.WaitAsync(AdmissionTestTimeouts.Timeout, _cancellation);
    internal Task<int> WaitForCleanupAsync() => base.Task.WaitAsync(AdmissionTestTimeouts.Timeout);
}