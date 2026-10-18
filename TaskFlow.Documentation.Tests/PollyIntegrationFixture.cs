namespace TaskFlow.Documentation.Tests;

using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Polly;
using Polly.Retry;
using Polly.Timeout;
using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Threading.Tasks.Flow;

[TestFixture]
internal sealed class PollyIntegrationFixture
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Backoff = TimeSpan.FromHours(1);
    private static readonly string[] InPlaceOrder = ["attempt-1", "attempt-2", "later"];
    private static readonly string[] ResubmissionOrder = ["attempt-1", "later", "attempt-2"];
    private static readonly string[] KeyedOrder = ["A1", "A2", "later-A"];

    [Test]
    public void GuideSnippets_MatchCompiledSource()
    {
        var guide = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "polly.md")).ReplaceLineEndings("\n");
        var source = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "PollyExamples.cs")).ReplaceLineEndings("\n");
        var snippets = Regex.Matches(guide, @"<!-- snippet: ([\w-]+) -->\n```csharp\n(.*?)\n```", RegexOptions.Singleline);
        Assert.That(snippets, Has.Count.EqualTo(7));
        foreach (Match snippet in snippets)
        {
            var region = Regex.Match(source, @"#region " + snippet.Groups[1].Value + @"\n(.*?)\n#endregion", RegexOptions.Singleline);
            Assert.That(region.Success, Is.True);
            Assert.That(snippet.Groups[2].Value, Is.EqualTo(region.Groups[1].Value), snippet.Groups[1].Value);
        }
        // Verify imports too: every C# fence is represented in compiled source.
        var imports = Regex.Match(guide, @"```csharp\n(using .*?)\n```", RegexOptions.Singleline).Groups[1].Value;
        Assert.That(imports, Is.Not.Empty);
        Assert.That(source, Does.Contain(imports));
        Assert.That(Regex.Matches(guide, @"```csharp"), Has.Count.EqualTo(8));
    }

    [Test]
    public async Task PublicExamples_ExecuteWithApplicationDelegates()
    {
        var calls = 0;
        ValueTask Save(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Interlocked.Increment(ref calls);
            return ValueTask.CompletedTask;
        }
        await RetryExample.RunAsync(Save, CancellationToken.None);
        await ResubmissionExample.RunAsync(Save, CancellationToken.None);
        await TimeoutExample.RunAsync(Save, CancellationToken.None);
        await DedicatedThreadExample.RunAsync(Save, CancellationToken.None);
        await DependencyInjectionExample.RunAsync(Save, CancellationToken.None);
        await KeyedDependencyInjectionExample.RunAsync(Save, CancellationToken.None);
        await KeyedExample.RunAsync((_, token) =>
        {
            token.ThrowIfCancellationRequested();
            Interlocked.Increment(ref calls);
            return ValueTask.FromResult(1);
        }, CancellationToken.None);
        Assert.That(calls, Is.EqualTo(9));
    }

    [TestCase(HttpStatusCode.ServiceUnavailable, 4)]
    [TestCase(HttpStatusCode.BadRequest, 1)]
    public async Task RetryFactory_FiltersFailuresAndCountsRetries(HttpStatusCode status, int expectedCalls)
    {
        var calls = 0;
        var clock = new ObservedTimeProvider();
        var pipeline = RetryExample.Create(maxRetries: 3, useJitter: false, timeProvider: clock);
        var work = pipeline.ExecuteAsync(_ =>
        {
            calls++;
            return ValueTask.FromException(new HttpRequestException("HTTP failure", null, status));
        }).AsTask();
        if (expectedCalls == 4)
        {
            for (var retry = 0; retry < 3; retry++)
            {
                var delay = TimeSpan.FromMilliseconds(200 * (1 << retry));
                await clock.TimerAsync(delay);
                clock.Advance(delay);
            }
        }
        await Assert.ThatAsync(async () => await work.WaitAsync(Guard), Throws.TypeOf<HttpRequestException>());
        Assert.That(calls, Is.EqualTo(expectedCalls));
    }

    [Test]
    public async Task RetryInsideDelegate_RetainsFifoPosition()
    {
        var clock = new ObservedTimeProvider();
        var pipeline = Retry(clock);
        await using var flow = new TaskFlow();
        var order = new List<string>();
        var attempts = 0;
        var first = flow.Enqueue(token => pipeline.ExecuteAsync(_ =>
        {
            order.Add("attempt-" + ++attempts);
            return attempts == 1 ? ValueTask.FromException(new HttpRequestException()) : ValueTask.CompletedTask;
        }, token).AsTask());
        await clock.TimerAsync(Backoff);
        var second = flow.Enqueue(() => order.Add("later"));
        Assert.That(second.IsCompleted, Is.False);
        clock.Advance(Backoff);
        await Task.WhenAll(first, second).WaitAsync(Guard);
        Assert.That(order, Is.EqualTo(InPlaceOrder));
    }

    [Test]
    public async Task RetryOutsideEnqueue_LosesOriginalPosition()
    {
        var clock = new ObservedTimeProvider();
        var pipeline = Retry(clock);
        await using var flow = new TaskFlow();
        var order = new List<string>();
        var attempts = 0;
        ValueTask Operation(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            order.Add("attempt-" + ++attempts);
            return attempts == 1 ? ValueTask.FromException(new HttpRequestException()) : ValueTask.CompletedTask;
        }
        var first = pipeline.ExecuteAsync(token => new ValueTask(flow.Enqueue(ct => Operation(ct).AsTask(), token))).AsTask();
        await clock.TimerAsync(Backoff);
        await flow.Enqueue(() => order.Add("later")).WaitAsync(Guard);
        clock.Advance(Backoff);
        await first.WaitAsync(Guard);
        Assert.That(order, Is.EqualTo(ResubmissionOrder));
    }

    [Test]
    public async Task KeyedRetry_HoldsKeyExecutionSlotAndOneAdmissionReservation()
    {
        var clock = new ObservedTimeProvider();
        var pipeline = Retry(clock);
        await using var owner = new TaskFlow();
        var admission = new BoundedAdmissionController(new BoundedAdmissionOptions
        {
            Capacity = 3,
            CapacityMode = TaskSchedulerCapacityMode.Outstanding
        });
        var scheduler = owner.WithKeyedScheduling<string>().WithConcurrencyLimit(2).WithBoundedAdmission(admission);
        var releaseB = Gate();
        var bStarted = Gate();
        var order = new ConcurrentQueue<string>();
        var attempts = 0;
        Func<CancellationToken, ValueTask<int>> operation = token => pipeline.ExecuteAsync(_ =>
        {
            order.Enqueue("A" + ++attempts);
            return attempts == 1 ? ValueTask.FromException<int>(new HttpRequestException()) : ValueTask.FromResult(1);
        }, token);
        var first = admission.Submit(scheduler, "A", operation, TaskSchedulerAdmissionMode.Reject);
        Assert.That(await first.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
        await clock.TimerAsync(Backoff);
        var sameKey = admission.Submit(scheduler, "A", () => { order.Enqueue("later-A"); return 2; }, TaskSchedulerAdmissionMode.Reject);
        var other = scheduler.Enqueue("B", (Func<CancellationToken, Task<int>>)(async token =>
        {
            bStarted.TrySetResult();
            await releaseB.Task.WaitAsync(token);
            return 3;
        }));
        try
        {
            await bStarted.Task.WaitAsync(Guard);
            Assert.That(await sameKey.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
            Assert.That(sameKey.Completion.IsCompleted, Is.False);
            var full = admission.Submit(scheduler, "C", () => 4, TaskSchedulerAdmissionMode.Reject);
            Assert.That(await full.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Full));
            await Assert.ThatAsync(async () => await full.Completion, Throws.TypeOf<AdmissionRejectedException>());
            clock.Advance(Backoff);
            await Task.WhenAll(first.Completion, sameKey.Completion).WaitAsync(Guard);
            Assert.That(order.ToArray(), Is.EqualTo(KeyedOrder));
        }
        finally { releaseB.TrySetResult(); }
        await other.WaitAsync(Guard);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CancellationDuringBackoff_StopsAttempts(bool shutdown)
    {
        var clock = new ObservedTimeProvider();
        var pipeline = Retry(clock);
        await using var flow = new TaskFlow();
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var work = flow.Enqueue(token => pipeline.ExecuteAsync(_ =>
        {
            Interlocked.Increment(ref attempts);
            return ValueTask.FromException(new HttpRequestException());
        }, token).AsTask(), cancellation.Token);
        await clock.TimerAsync(Backoff);
        if (shutdown)
            await flow.DisposeAsync().AsTask().WaitAsync(Guard);
        else
            await cancellation.CancelAsync();
        await Assert.ThatAsync(async () => await work.WaitAsync(Guard), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(attempts, Is.EqualTo(1));
        if (!shutdown)
            await flow.Enqueue(() => { }).WaitAsync(Guard);
    }

    [Test]
    public async Task TimeoutComposition_RetriesAttemptTimeoutButNotTotalTimeout()
    {
        var clock = new ObservedTimeProvider();
        var pipeline = new ResiliencePipelineBuilder { TimeProvider = clock }
            .AddTimeout(TimeSpan.FromSeconds(12))
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<TimeoutRejectedException>(),
                MaxRetryAttempts = 10,
                Delay = TimeSpan.Zero
            })
            .AddTimeout(TimeSpan.FromSeconds(5)).Build();
        await using var flow = new TaskFlow();
        var attempts = 0;
        var work = flow.Enqueue(token => pipeline.ExecuteAsync(async attemptToken =>
        {
            Interlocked.Increment(ref attempts);
            await Task.Delay(Timeout.InfiniteTimeSpan, attemptToken);
        }, token).AsTask());
        await clock.TimerAsync(TimeSpan.FromSeconds(12));
        await clock.TimerAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(5));
        await clock.TimerAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(5));
        await clock.TimerAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(2));
        await Assert.ThatAsync(async () => await work.WaitAsync(Guard), Throws.TypeOf<TimeoutRejectedException>());
        Assert.That(attempts, Is.EqualTo(3));
    }

    [Test]
    public async Task TaskFlowTimeout_IncludesQueueWaitBeforePollyStarts()
    {
        var clock = new ObservedTimeProvider();
        var pipeline = TimeoutExample.Create();
        await using var flow = new TaskFlow();
        var started = Gate();
        var release = Gate();
        var blocker = flow.Enqueue(async token =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(token);
        });
        var calls = 0;
        try
        {
            await started.Task.WaitAsync(Guard);
            var scheduler = flow.WithTimeout(TimeSpan.FromSeconds(3), clock);
            var work = scheduler.Enqueue(token => pipeline.ExecuteAsync(_ =>
            {
                Interlocked.Increment(ref calls);
                return ValueTask.CompletedTask;
            }, token).AsTask());
            await clock.TimerAsync(TimeSpan.FromSeconds(3));
            clock.Advance(TimeSpan.FromSeconds(3));
            await Assert.ThatAsync(async () => await work.WaitAsync(Guard), Throws.TypeOf<TimeoutException>());
            Assert.That(calls, Is.Zero);
        }
        finally { release.TrySetResult(); }
        await blocker.WaitAsync(Guard);
        // This operation also proves that the timed-out envelope has settled.
        await flow.Enqueue(() => { }).WaitAsync(Guard);
        Assert.That(calls, Is.Zero);
    }

    [Test]
    public async Task PollyTimeout_DoesNotStopCancellationIgnoringCodeOrReleaseLane()
    {
        var clock = new ObservedTimeProvider();
        var pipeline = new ResiliencePipelineBuilder { TimeProvider = clock }
            .AddTimeout(TimeSpan.FromSeconds(5)).Build();
        await using var flow = new TaskFlow();
        var started = Gate();
        var canceled = Gate();
        var release = Gate();
        var work = flow.Enqueue<int>(async (_, token) => await pipeline.ExecuteAsync(async attemptToken =>
        {
            using var registration = attemptToken.Register(() => canceled.TrySetResult());
            started.TrySetResult();
            await release.Task; // Deliberately ignore cancellation.
            return 42;
        }, token), null, CancellationToken.None);
        try
        {
            await started.Task.WaitAsync(Guard);
            await clock.TimerAsync(TimeSpan.FromSeconds(5));
            var later = flow.Enqueue(() => 7);
            clock.Advance(TimeSpan.FromSeconds(5));
            await canceled.Task.WaitAsync(Guard);
            Assert.That(work.IsCompleted, Is.False);
            Assert.That(later.IsCompleted, Is.False);
            release.TrySetResult();
            Assert.That(await work.WaitAsync(Guard), Is.EqualTo(42));
            Assert.That(await later.WaitAsync(Guard), Is.EqualTo(7));
        }
        finally { release.TrySetResult(); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DedicatedRetry_CaptureControlsAttemptPlacement(bool capture)
    {
        var clock = new ObservedTimeProvider();
        var pipeline = Retry(clock);
        await using var flow = new DedicatedThreadTaskFlow("polly-test");
        var threads = new List<int>();
        var contexts = new List<SynchronizationContext?>();
        var work = flow.Enqueue(async token =>
        {
            var context = ResilienceContextPool.Shared.Get(continueOnCapturedContext: capture, cancellationToken: token);
            try
            {
                await pipeline.ExecuteAsync(_ =>
                {
                    threads.Add(Environment.CurrentManagedThreadId);
                    contexts.Add(SynchronizationContext.Current);
                    return threads.Count == 1 ? ValueTask.FromException(new HttpRequestException()) : ValueTask.CompletedTask;
                }, context);
            }
            finally { ResilienceContextPool.Shared.Return(context); }
        });
        await clock.TimerAsync(Backoff);
        // Wait for the operation to yield to its pump before making the retry delay ready.
        var parked = Gate();
        contexts[0]!.Post(_ => parked.TrySetResult(), null);
        await parked.Task.WaitAsync(Guard);
        clock.Advance(Backoff);
        await work.WaitAsync(Guard);
        Assert.That(contexts[0], Is.Not.Null);
        if (capture)
        {
            Assert.That(threads[1], Is.EqualTo(threads[0]));
            Assert.That(contexts[1], Is.SameAs(contexts[0]));
        }
        else
        {
            Assert.That(threads[1], Is.Not.EqualTo(threads[0]));
            Assert.That(contexts[1], Is.Null);
        }
    }

    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static ResiliencePipeline Retry(TimeProvider clock) =>
        new ResiliencePipelineBuilder { TimeProvider = clock }.AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(),
            MaxRetryAttempts = 1,
            Delay = Backoff,
            UseJitter = false
        }).Build();

    // Signal only after the fake timer is registered, so advancing cannot race its creation.
    private sealed class ObservedTimeProvider : TimeProvider
    {
        private readonly FakeTimeProvider _clock = new();
        private readonly Channel<TimeSpan> _timers = Channel.CreateUnbounded<TimeSpan>();

        public override DateTimeOffset GetUtcNow() => _clock.GetUtcNow();
        public override long GetTimestamp() => _clock.GetTimestamp();
        public override long TimestampFrequency => _clock.TimestampFrequency;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = _clock.CreateTimer(callback, state, dueTime, period);
            Signal(dueTime);
            return new ObservedTimer(timer, Signal);
        }

        private void Signal(TimeSpan dueTime)
        {
            if (dueTime != Timeout.InfiniteTimeSpan)
                _timers.Writer.TryWrite(dueTime);
        }

        public async Task TimerAsync(TimeSpan expected)
        {
            var due = await _timers.Reader.ReadAsync().AsTask().WaitAsync(Guard);
            Assert.That(due, Is.EqualTo(expected));
        }

        public void Advance(TimeSpan elapsed) => _clock.Advance(elapsed);

        private sealed class ObservedTimer(ITimer inner, Action<TimeSpan> signal) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                var changed = inner.Change(dueTime, period);
                if (changed)
                    signal(dueTime);
                return changed;
            }

            public void Dispose() => inner.Dispose();
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
}