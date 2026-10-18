#pragma warning disable CA2007 // Application examples intentionally capture the current context.

namespace TaskFlow.Documentation.Tests;

using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Registry;
using Polly.Retry;
using Polly.Timeout;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Flow;

#region retry
internal static class RetryExample
{
    public static ResiliencePipeline Create(
        int maxRetries = 3, bool useJitter = true, TimeProvider? timeProvider = null) =>
        new ResiliencePipelineBuilder { TimeProvider = timeProvider ?? TimeProvider.System }
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(
                    error => error.StatusCode is HttpStatusCode.RequestTimeout
                        or HttpStatusCode.TooManyRequests
                        or HttpStatusCode.ServiceUnavailable),
                MaxRetryAttempts = maxRetries,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = useJitter
            })
            .Build();

    public static async Task RunAsync(
        Func<CancellationToken, ValueTask> saveAsync,
        CancellationToken cancellationToken)
    {
        var pipeline = Create(maxRetries: 3, useJitter: true);
        await using var flow = new TaskFlow();
        await flow.Enqueue(
            scheduledToken => pipeline.ExecuteAsync(saveAsync, scheduledToken).AsTask(),
            cancellationToken);
    }
}
#endregion

#region resubmit
internal static class ResubmissionExample
{
    // Deliberately resubmits: this does not retain one queue position.
    public static async Task RunAsync(
        Func<CancellationToken, ValueTask> saveAsync,
        CancellationToken cancellationToken)
    {
        var pipeline = RetryExample.Create();
        await using var flow = new TaskFlow();
        await pipeline.ExecuteAsync(
            token => new ValueTask(flow.Enqueue(ct => saveAsync(ct).AsTask(), token)),
            cancellationToken);
    }
}
#endregion

#region keyed
internal static class KeyedExample
{
    public static async Task RunAsync(
        Func<string, CancellationToken, ValueTask<int>> readVersionAsync,
        CancellationToken cancellationToken)
    {
        var pipeline = RetryExample.Create();
        await using var owner = new TaskFlow();
        var admission = new BoundedAdmissionController(new BoundedAdmissionOptions
        {
            Capacity = 16,
            CapacityMode = TaskSchedulerCapacityMode.Outstanding
        });
        var scheduler = owner.WithKeyedScheduling<string>()
            .WithConcurrencyLimit(2)
            .WithBoundedAdmission(admission);

        Func<string, CancellationToken, ValueTask<int>> operation =
            (key, token) => pipeline.ExecuteAsync(
                attemptToken => readVersionAsync(key, attemptToken), token);

        var first = admission.Submit(scheduler, "document-17", operation,
            TaskSchedulerAdmissionMode.Wait, cancellationToken);
        await first.Admission;
        Task<int> sameKey = scheduler.Enqueue("document-17", operation, cancellationToken);
        Task<int> otherKey = scheduler.Enqueue("document-18", operation, cancellationToken);
        await Task.WhenAll(first.Completion, sameKey, otherKey);
    }
}
#endregion

#region timeout
internal static class TimeoutExample
{
    public static ResiliencePipeline Create() =>
        new ResiliencePipelineBuilder()
            .AddTimeout(TimeSpan.FromSeconds(12)) // Total execution, including backoff.
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>(error =>
                        error.StatusCode == HttpStatusCode.ServiceUnavailable)
                    .Handle<TimeoutRejectedException>(),
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            })
            .AddTimeout(TimeSpan.FromSeconds(5)) // Each attempt.
            .Build();

    public static async Task RunAsync(
        Func<CancellationToken, ValueTask> readAsync,
        CancellationToken cancellationToken)
    {
        var pipeline = Create();
        await using var owner = new TaskFlow();
        var scheduler = owner.WithTimeout(TimeSpan.FromSeconds(20));
        await scheduler.Enqueue(
            scheduledToken => pipeline.ExecuteAsync(readAsync, scheduledToken).AsTask(),
            cancellationToken);
    }
}
#endregion

#region dedicated
internal static class DedicatedThreadExample
{
    public static async Task RunAsync(
        Func<CancellationToken, ValueTask> deviceCallAsync,
        CancellationToken cancellationToken)
    {
        var pipeline = RetryExample.Create();
        await using var flow = new DedicatedThreadTaskFlow("device-io");
        await flow.Enqueue(async scheduledToken =>
        {
            var context = ResilienceContextPool.Shared.Get(
                continueOnCapturedContext: true,
                cancellationToken: scheduledToken);
            try
            {
                await pipeline.ExecuteAsync(
                    ctx => deviceCallAsync(ctx.CancellationToken), context);
            }
            finally
            {
                ResilienceContextPool.Shared.Return(context);
            }
        }, cancellationToken);
    }
}
#endregion

#region di
internal static class DependencyInjectionExample
{
    public static async Task RunAsync(
        Func<CancellationToken, ValueTask> saveAsync,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddResiliencePipeline("storage", builder =>
            builder.AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(
                    error => error.StatusCode == HttpStatusCode.ServiceUnavailable),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            }));
        services.AddTaskFlow("storage");

        await using var root = services.BuildServiceProvider();
        var pipeline = root.GetRequiredService<ResiliencePipelineProvider<string>>()
            .GetPipeline("storage");
        await using var flow = root.GetRequiredService<ITaskFlowFactory>()
            .CreateTaskFlow("storage");
        await flow.Enqueue(
            scheduledToken => pipeline.ExecuteAsync(saveAsync, scheduledToken).AsTask(),
            cancellationToken);
    }
}
#endregion

#region keyed-di
internal static class KeyedDependencyInjectionExample
{
    public static async Task RunAsync(
        Func<CancellationToken, ValueTask> saveAsync,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddResiliencePipeline("storage", builder =>
            builder.AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(
                    error => error.StatusCode == HttpStatusCode.ServiceUnavailable),
                MaxRetryAttempts = 3
            }));
        services.AddScoped<TaskFlow>();
        services.AddScoped<ITaskScheduler>(provider =>
            provider.GetRequiredService<TaskFlow>()
                .WithKeyedScheduling<string>().WithConcurrencyLimit(2));

        await using var root = services.BuildServiceProvider();
        await using var scope = root.CreateAsyncScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<ITaskScheduler>();
        var pipeline = root.GetRequiredService<ResiliencePipelineProvider<string>>()
            .GetPipeline("storage");
        await scheduler.Enqueue("document-17",
            token => pipeline.ExecuteAsync(saveAsync, token).AsTask(), cancellationToken);
    }
}
#endregion