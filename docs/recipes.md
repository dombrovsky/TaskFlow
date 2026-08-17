---
layout: page
title: Recipes
permalink: /recipes/
---

# Recipes

These examples use both caller-observed operation tasks and deliberate fire-and-forget submissions whose lifetime is owned by a flow.

If you are choosing between TaskFlow and other primitives, read [Choosing TaskFlow](choosing-taskflow.md) first.

## Serialize a non-thread-safe resource

```csharp
public sealed class SerializedReadingStore : IAsyncDisposable
{
    private readonly IReadingStore _inner;
    private readonly TaskFlow _flow = new();

    public SerializedReadingStore(IReadingStore inner)
    {
        _inner = inner;
    }

    public Task AppendAsync(
        Reading reading,
        CancellationToken cancellationToken = default)
    {
        return _flow.Enqueue(
            token => _inner.AppendAsync(reading, token),
            cancellationToken);
    }

    public ValueTask DisposeAsync() => _flow.DisposeAsync();
}
```

All callers remain asynchronous while access to `_inner` stays FIFO and non-concurrent. Unlike a manually managed `SemaphoreSlim`, the flow also owns shutdown and returns a task for each queued operation.

## Preserve order from synchronous events

```csharp
public sealed class ReadingSubscriber : IAsyncDisposable
{
    private readonly IReadingSource _source;
    private readonly IReadingSink _sink;
    private readonly TaskFlow _flow = new();
    private readonly ITaskScheduler _events;

    public ReadingSubscriber(IReadingSource source, IReadingSink sink)
    {
        _source = source;
        _sink = sink;
        _events = _flow.OnError<Exception>(
            exception => Console.Error.WriteLine(exception));
        source.ReadingReceived += OnReadingReceived;
    }

    private void OnReadingReceived(object? sender, ReadingEventArgs args)
    {
        Reading reading = args.Reading;

        _ = _events.Enqueue(
            token => _sink.HandleAsync(reading, token));
    }

    public async ValueTask DisposeAsync()
    {
        _source.ReadingReceived -= OnReadingReceived;
        await _flow.DisposeAsync();
    }
}
```

The synchronous event handler only captures the event data and enqueues the asynchronous work. It returns immediately without becoming `async void`, and TaskFlow preserves callback submission order. The operation tasks are intentionally discarded; `OnError` reports failures, while disposing `_flow` controls the lifetime of every accepted callback. Unsubscribe before disposal so no callback can submit during shutdown.

## Avoid duplicate credential refreshes

```csharp
public sealed class CachedTokenProvider : IAsyncDisposable
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(1);

    private readonly ITokenClient _client;
    private readonly TaskFlow _flow = new();
    private AccessToken? _cached;

    public CachedTokenProvider(ITokenClient client)
    {
        _client = client;
    }

    public async Task<AccessToken> GetAsync(
        CancellationToken cancellationToken = default)
    {
        AccessToken? cached = _cached;
        if (IsUsable(cached))
        {
            return cached!;
        }

        return await _flow.Enqueue(RefreshAsync, cancellationToken);

        async Task<AccessToken> RefreshAsync(CancellationToken token)
        {
            if (IsUsable(_cached))
            {
                return _cached!;
            }

            _cached = await _client.RequestAsync(token);
            return _cached;
        }
    }

    public ValueTask DisposeAsync() => _flow.DisposeAsync();

    private static bool IsUsable(AccessToken? token) =>
        token is not null &&
        token.ExpiresAt - RefreshMargin > DateTimeOffset.UtcNow;
}
```

The second check is essential because another caller may refresh the value while this request waits in the lane. The explicitly typed local function also avoids ambiguous `Task<T>` and `ValueTask<T>` overload selection.

## Latest request wins

```csharp
public sealed class SearchController : IAsyncDisposable
{
    private readonly ISearchClient _client;
    private readonly IResultView _view;
    private readonly TaskFlow _flow = new();
    private readonly ITaskScheduler _latestSearch;

    public SearchController(ISearchClient client, IResultView view)
    {
        _client = client;
        _view = view;
        _latestSearch = _flow.CreateCancelPrevious();
    }

    public Task SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        return _latestSearch.Enqueue(async token =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), token);
            SearchResults results = await _client.SearchAsync(query, token);
            await _view.ShowAsync(results, token);
        }, cancellationToken);
    }

    public ValueTask DisposeAsync() => _flow.DisposeAsync();
}
```

Each submission cancels unfinished older submissions. The initial delay means rapid updates usually leave only the latest delegate past the delay, but this is still cooperative cancellation rather than a native trailing-edge debounce policy.

## Own a recoverable background loop

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks.Flow;

public sealed class InboxPump : IHostedService
{
    private readonly IInbox _inbox;
    private readonly ILogger<InboxPump> _logger;
    private readonly TaskFlow _lifetime = new();

    public InboxPump(IInbox inbox, ILogger<InboxPump> logger)
    {
        _inbox = inbox;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = _lifetime.Enqueue(RunAsync);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        _lifetime.DisposeAsync().AsTask().WaitAsync(cancellationToken);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using (var iteration = new TaskFlow())
            {
                ITaskScheduler work = iteration.OnError<Exception>(
                    exception => _logger.LogWarning(
                        exception,
                        "Inbox iteration failed"),
                    _ => !cancellationToken.IsCancellationRequested);

                _ = work.Enqueue(
                    _inbox.ProcessAvailableAsync,
                    cancellationToken);
            }

            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        }
    }
}
```

`StartAsync` deliberately discards the outer operation task and returns promptly to the host. The outer flow owns the loop and supplies its shutdown token; `StopAsync` disposes that flow and waits within the host's shutdown budget.

Each pass creates an inner flow, submits one fire-and-forget operation, and disposes the inner flow before delaying. Inner disposal waits for that iteration but does not propagate its operation failure into the outer loop, so the next iteration still runs. `OnError` reports failures before rethrowing them into the intentionally ignored per-iteration task. The delay belongs to the outer operation, so stopping the host cancels both the current iteration and the wait before the next one.

## Compose operational policies

```csharp
public sealed class ObservableDeviceClient : IAsyncDisposable
{
    private readonly TaskFlow _flow = new();
    private readonly ITaskScheduler _operations;

    public ObservableDeviceClient(
        ILogger logger,
        CancellationToken lifetimeToken)
    {
        _operations = _flow
            .WithLogging(logger)
            .WithTimeout(TimeSpan.FromSeconds(15))
            .OnError<Exception>(exception =>
                logger.LogError(exception, "Device synchronization failed"))
            .CreateCancellationScope(lifetimeToken)
            .WithOperationName("device.sync");
    }

    public Task SynchronizeAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        return _operations.Enqueue(operation, cancellationToken);
    }

    public ValueTask DisposeAsync() => _flow.DisposeAsync();
}
```

The outer operation-name decorator passes its annotation through the chain so logging and timeout diagnostics can use it. Only `_flow` owns the lane and is disposed.
