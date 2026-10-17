# Execution and completion cancellation

C1 and C2 form one cancellation contract. Execution middleware must preserve the terminal's effective token, and it must be able to forward a scoped replacement without creating another logical operation.

## Four token roles

| Role | Meaning | Public access |
| --- | --- | --- |
| Caller (C) | Original token passed to Enqueue | `CallerCancellationToken` |
| Producer (P) | Token selected before terminal submission, including enqueue transformations | `ProducerCancellationToken` |
| Execution (E) | Token supplied when the terminal invokes the envelope; built-in flows add shutdown cancellation | Initial execution and normal completion `CancellationToken` |
| Attempt (A) | Token selected for a particular downstream middleware invocation | `CancellationToken` on a context returned by `WithCancellationToken` |

A custom terminal can supply an E unrelated to C or P. The pipeline forwards E rather than trying to reconstruct it. If enqueue middleware intentionally detaches P from C, execution does not implicitly reattach C.

`ProducerCancellationToken` is a snapshot taken when each context is created. A copied context retains that snapshot even if an early caller timeout races later terminal submission. On a path that has not reached terminal submission, the existing producer field is initialized to C; that value is a fallback, not a finalized producer selection.

## Immutable token replacement

Execution middleware can create a scoped attempt token and forward it through ordinary middleware to the actual user delegate:

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

`WithCancellationToken` creates an immutable context copy. It preserves the exact operation identity, original state, annotation scope, registration-local state slot, callback scheduler, caller token, and producer snapshot. It does not create another operation, admission reservation, or local-state array. The method itself does not create, link, cancel, or dispose any token source. The middleware above explicitly owns its source and links the incoming token so caller/producer/shutdown cancellation remains effective.

Replacements apply downstream for that invocation. If one stage supplies X, a pass-through stage sees X; a later stage can supply Y, and the user delegate sees Y. The original contexts remain unchanged. A retry starts each new attempt from its own original context and a fresh token, rather than reusing the previous attempt's canceled token. Registration-local state belongs to the logical operation and is shared across attempts, not recreated per attempt.

Execution and completion continuations reject contexts belonging to another operation. Copying a context does not serialize concurrent continuation calls or make hedging safe. No Polly dependency or Polly-specific abstraction is introduced; scoped timeouts and sequential retry decorators can use this API independently.

## Completion and early outcomes

Normal terminal completion starts with E, regardless of the last attempt's A. Attempt token sources can be disposed before completion runs. Completion middleware may use `WithCancellationToken` to replace the token for its own downstream invocation; subsequent stages preserve that replacement. If a completion callback throws before continuing, later callbacks receive its incoming token and the replacement exception. If it throws after continuing, the pipeline does not repeat downstream completion.

For enqueue-side rejection, timeout, or terminal submission failure, no terminal execution token is available to that completion winner. It receives the captured P, or the existing C initialization if terminal submission has not occurred. Completion runs once, even if timed-out underlying work subsequently reaches the terminal. Preterminal completion does not promise terminal thread or synchronization-context affinity. Cancellation never causes the pipeline to skip completion processing.

Disposal cooperatively cancels middleware and delegates observing E, and A when its source is linked to E. A middleware can deliberately replace cancellation; the copy API does not enforce non-removable shutdown. Disposal still waits for underlying work that ignores cancellation. Copies and retries retain FIFO envelope ordering, quiescence, admission accounting, and lifetime ownership.

## Behavioral compatibility

Previously, `TaskSchedulerOperationContext.CancellationToken` was documented as P and execution/completion middleware reset it to P. It now represents the current invocation's effective token: E initially, or an explicit downstream replacement. This intentionally changes both execution and normal completion cancellation behavior. Middleware that needs the preterminal token must use `ProducerCancellationToken`; `CallerCancellationToken` retains its original semantics. Consumers that compare token identity or use cancellation-sensitive completion logic must review that assumption.

`TaskSchedulerInterceptionContext.CancellationToken` now follows the effective execution token too, including upstream attempt replacements. Interceptors that observe cancellation can therefore observe shutdown or a scoped attempt timeout. Completion callbacks within an execution interceptor remain part of its execution scope; they are distinct from the separately invoked completion-middleware phase.

C4 (#48), custom scheduler callback placement, is independent and unchanged by this work. No TaskFlow.Polly integration is implemented.