---
layout: page
title: Recipes
permalink: /recipes/
---

# Recipes

These examples focus on ownership and observable operation tasks rather than treating queued work as untracked fire-and-forget work.

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
    private readonly IReadingSink _sink;
    private readonly TaskFlow _flow = new();

    public ReadingSubscriber(IReadingSource source, IReadingSink sink)
    {
        _sink = sink;
        source.ReadingReceived += OnReadingReceived;
    }

    private async void OnReadingReceived(object? sender, ReadingEventArgs args)
    {
        Reading reading = args.Reading;

        try
        {
            await _flow.Enqueue(
                token => _sink.HandleAsync(reading, token));
        }
        catch (OperationCanceledException)
        {
            // Expected when the subscriber is disposed.
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
        }
    }

    public ValueTask DisposeAsync() => _flow.DisposeAsync();
}
```

The event handler copies the event data and calls `Enqueue` before its first suspension, so TaskFlow preserves callback submission order. Event handlers are the conventional exception to avoiding `async void`; this one catches every operation outcome locally. In production, unsubscribe the event before disposing the flow so no callback can submit during shutdown.

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
public sealed class InboxPump : IAsyncDisposable
{
    private readonly IInbox _inbox;
    private readonly ILogger _logger;
    private readonly TaskFlow _flow = new();
    private Task? _completion;

    public InboxPump(IInbox inbox, ILogger logger)
    {
        _inbox = inbox;
        _logger = logger;
    }

    public Task Completion => _completion ?? Task.CompletedTask;

    public void Start()
    {
        _completion ??= _flow.Enqueue(RunAsync);
    }

    public async ValueTask DisposeAsync()
    {
        await _flow.DisposeAsync();

        if (_completion is null)
        {
            return;
        }

        try
        {
            await _completion;
        }
        catch (OperationCanceledException)
        {
            // Disposal canceled the loop: normal shutdown.
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await _inbox.ProcessAvailableAsync(cancellationToken);
            }
            catch (TransientInboxException exception)
                when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(exception, "Inbox iteration failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
        }
    }
}
```

Recoverable failures are caught per iteration so the loop survives them. `Completion` exposes unexpected terminal failures, while disposal cancellation is handled as normal shutdown. An outer `OnError` decorator can report a terminal failure but cannot restart a failed loop.

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
