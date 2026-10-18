namespace TaskFlow.Extensions.Microsoft.DependencyInjection.Tests;

using global::Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Polly;
using Polly.Registry;
using Polly.Retry;
using System.Net;
using System.Threading.Tasks.Flow;

[TestFixture]
internal sealed class PollyDependencyInjectionFixture
{
    [Test]
    public async Task NamedPipeline_IsSharedAndRetriesInsideFactoryFlow()
    {
        var retryOptions = new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(
                error => error.StatusCode == HttpStatusCode.ServiceUnavailable),
            MaxRetryAttempts = 3,
            Delay = TimeSpan.Zero
        };
        var services = new ServiceCollection();
        services.AddResiliencePipeline("storage", builder => builder.AddRetry(retryOptions));
        services.AddTaskFlow("storage");

        await using var root = services.BuildServiceProvider();
        var provider = root.GetRequiredService<ResiliencePipelineProvider<string>>();
        var pipeline = provider.GetPipeline("storage");
        await using var flow = root.GetRequiredService<ITaskFlowFactory>().CreateTaskFlow("storage");
        var calls = 0;
        ValueTask SaveAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ++calls == 1
                ? ValueTask.FromException(new HttpRequestException("Unavailable", null, HttpStatusCode.ServiceUnavailable))
                : ValueTask.CompletedTask;
        }

        await flow.Enqueue(token => pipeline.ExecuteAsync(SaveAsync, token).AsTask());
        Assert.That(calls, Is.EqualTo(2));
        Assert.That(provider.GetPipeline("storage"), Is.SameAs(pipeline));
    }

    [Test]
    public async Task NamedFactoryWrapper_RejectsNativeKeyedAndAdmissionCalls()
    {
        var admission = new BoundedAdmissionController(new BoundedAdmissionOptions { Capacity = 2 });
        var services = new ServiceCollection();
        services.AddTaskFlow("keyed", configureSchedulerChain: (scheduler, _) =>
            scheduler.WithKeyedScheduling<string>().WithBoundedAdmission(admission));
        await using var root = services.BuildServiceProvider();
        await using var flow = root.GetRequiredService<ITaskFlowFactory>().CreateTaskFlow("keyed");

        Assert.That(() => flow.Enqueue("document-17", () => 1), Throws.TypeOf<InvalidOperationException>());
        Assert.That(() => admission.Submit(flow, "document-17", () => 1, TaskSchedulerAdmissionMode.Wait),
            Throws.TypeOf<InvalidOperationException>());
        Assert.That(() => admission.Submit(flow, () => 1, TaskSchedulerAdmissionMode.Wait),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public async Task ScopedNativeSnapshot_SupportsKeyedAdmissionAndScopeOwnsOwner()
    {
        var admission = new BoundedAdmissionController(new BoundedAdmissionOptions { Capacity = 2 });
        var services = new ServiceCollection();
        services.AddScoped<TaskFlow>();
        services.AddScoped<ITaskScheduler>(provider =>
            provider.GetRequiredService<TaskFlow>()
                .WithKeyedScheduling<string>().WithConcurrencyLimit(2)
                .WithBoundedAdmission(admission));
        await using var root = services.BuildServiceProvider();
        TaskFlow owner;
        await using (var scope = root.CreateAsyncScope())
        {
            owner = scope.ServiceProvider.GetRequiredService<TaskFlow>();
            var scheduler = scope.ServiceProvider.GetRequiredService<ITaskScheduler>();
            await scheduler.Enqueue("document-17", token => SaveAsync(token));
            var submission = admission.Submit(scheduler, "document-17", () => 42, TaskSchedulerAdmissionMode.Wait);
            Assert.That(await submission.Admission, Is.EqualTo(TaskSchedulerAdmissionResult.Accepted));
            Assert.That(await submission.Completion, Is.EqualTo(42));
        }
        Assert.That(() => owner.Enqueue(() => 1), Throws.TypeOf<ObjectDisposedException>());

        static Task SaveAsync(CancellationToken token) => Task.CompletedTask;
    }
}