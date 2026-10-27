# Execution and completion cancellation

The terminal scheduler is the `ITaskScheduler` at the end of the enqueue pipeline. Its execution token may include cancellation added by the scheduler, such as flow shutdown. Middleware preserves this token unless it deliberately replaces it for downstream work.

## Token roles

`TaskSchedulerOperationContext` distinguishes four roles:

| Role | Meaning | Public access |
| --- | --- | --- |
| Caller | Original token passed to `Enqueue` | `CallerCancellationToken` |
| Producer | Token selected by enqueue middleware for terminal submission | `ProducerCancellationToken` |
| Execution | Token supplied when the terminal invokes the operation | Initial execution and normal completion `CancellationToken` |
| Attempt | Replacement token supplied by execution middleware | `CancellationToken` on a context copied with `WithCancellationToken` |

These roles can have different tokens. Keeping caller and producer cancellation separate allows an explicit enqueue ownership boundary (`InvokeWithCallerWait`) to end a caller's wait before producer termination. Ordinary middleware must await all downstream work it starts. The pipeline does not reattach caller cancellation after that choice.

Each context snapshots the producer token when created. Copies retain that value even if an early timeout races later submission. Before terminal submission, it falls back to the original caller token.

## Scoped replacements

A timeout middleware can link incoming cancellation to an attempt's timeout:

```csharp
public async ValueTask<TResult> InvokeAsync<TResult>(
    TaskSchedulerOperationContext context,
    TaskSchedulerExecutionDelegate<TResult> continuation)
{
    using var attempt = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
    attempt.CancelAfter(TimeSpan.FromSeconds(10));
    return await continuation(context.WithCancellationToken(attempt.Token)).ConfigureAwait(true);
}
```

`WithCancellationToken` returns an immutable context copy for the same operation and registration. It preserves state, annotations, local state, callback scheduler, caller token, and producer snapshot. It creates no operation or admission reservation and does not manage token sources. Middleware owns its source and must link incoming cancellation when it should remain effective.

Replacements affect only the downstream invocation; later middleware can replace them again without changing upstream contexts. Each retry should start from its original context with a fresh token, avoiding cancellation left by a previous attempt. Local state remains shared across attempts. Continuations reject another operation's context; copies do not serialize concurrent calls.

## Completion and disposal

Normal completion starts with the terminal's execution token, not the last attempt token, whose source may already be disposed. Completion middleware can replace its own downstream token. Middleware must await or return its continuation.

If a completion callback throws before continuing, the pipeline passes its incoming token and failure outcome to later callbacks. Throwing after continuing does not repeat downstream callbacks. Upstream callbacks can inspect and transform returned outcomes.

Enqueue-side rejection, timeout, or submission failure uses the producer snapshot, with the caller fallback described above. Completion runs once even if timed-out work later reaches the terminal. Early completion does not guarantee the terminal's thread or synchronization context. Cancellation does not skip completion callbacks.

A caller-wait boundary can finish before its producer, including after caller
cancellation. Nested downstream task completion is not a producer-termination
signal: an inner caller wait may have finished while execution continues. Keep
producer cancellation sources alive with the owned work and observe late faults.

Disposal requests cancellation and waits for underlying work to finish. Cancellation remains cooperative. Copies and retries keep the same queue position, admission reservation, and lifetime ownership.

## Compatibility

`CancellationToken` represents effective cancellation so middleware can observe shutdown and scoped timeouts consistently. Code relying on its earlier producer-token meaning must use `ProducerCancellationToken` when it needs that token. `CallerCancellationToken` always exposes the original caller token.

`TaskSchedulerInterceptionContext.CancellationToken` also follows the effective execution token. An interceptor's success, error, and finally callbacks belong to its execution scope, separately from completion middleware.