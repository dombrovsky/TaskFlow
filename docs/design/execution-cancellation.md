# Execution middleware cancellation

Execution middleware and the user delegate receive the effective cancellation token supplied by the terminal scheduler. Each execution stage forwards the token it receives instead of resetting it to the earlier producer token. Built-in flows link producer cancellation with flow shutdown; a custom terminal may supply its own linked or replacement token. Middleware delays and other cooperative work should observe this effective token.

`TaskSchedulerOperationContext` distinguishes three tokens:

| Property | Execution | Completion |
| --- | --- | --- |
| `CallerCancellationToken` | Original submitting caller token | Original submitting caller token |
| `ProducerCancellationToken` | Token captured immediately before terminal scheduling | Same preterminal producer token |
| `CancellationToken` | Effective terminal execution token forwarded through middleware | Preterminal producer token |

Enqueue middleware can replace the producer token using `TaskSchedulerEnqueueContext<TResult>.WithCancellationToken` without changing the original caller token. For example, shared producer work may ignore cancellation of an individual caller's wait while still observing flow shutdown during execution. Before terminal scheduling is reached, the producer token defaults to the original caller token.

## Compatibility decision

Execution contexts previously exposed the preterminal producer token in `CancellationToken`. That behavior could hide shutdown cancellation whenever execution middleware was present. `CancellationToken` now exposes the effective execution token. Middleware that specifically needs the earlier producer token must use `ProducerCancellationToken`; middleware that needs the original caller token must use `CallerCancellationToken`. This is an intentional behavioral correction, not a behavior-neutral change.

The existing `TaskSchedulerInterceptionContext.CancellationToken` contract continues to report the preterminal producer token. The interception adapter reads `ProducerCancellationToken` explicitly; its execution continuation still forwards the effective execution token.

Execution contexts do not currently expose a public token-copy/override API. This fix does not add one. If token transformations become supported during execution, subsequent stages must preserve the forwarded token rather than resetting it to the producer token.

## Completion and lifetime

Completion keeps its existing producer-token semantics. It can run after enqueue rejection, when no execution token exists, and it must run for canceled operations as well as successful operations. Its token is an observation, not a reason for the pipeline to skip completion. Terminal-specific cancellation linkage is therefore not introduced into completion contexts by this fix.

Disposal cooperatively cancels middleware and delegates that observe the effective execution token. It still waits for underlying work that ignores cancellation and drains accepted work in queue order. Registration-local state remains shared between execution and completion for each compound middleware registration.