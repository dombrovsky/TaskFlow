# Middleware lifetime accounting

## Ownership

A built-in flow owns its middleware lifetime. Factory registrations on other
terminals require an explicit lifetime supplied by their owner. Snapshots share
registrations; appending middleware does not recreate existing instances.
Only middleware created through a registration factory is owned by the lifetime.

Disposal must come from the external owner. Disposing an owner from its delegates,
middleware callbacks, factories, or cleanup is unsupported: shutdown waits for the
very work or registration making that call. There is no ambient ownership state
or automatic self-disposal detection.

## Registration and shutdown

Registration reserves its cleanup position and enters the lifetime under the same
lock used to close entry. The factory runs outside that lock. A result racing
shutdown is attached before the caller receives ObjectDisposedException, so owned
resources are still cleaned up. A failed factory leaves an empty reserved slot.
Nested registration follows the same rules without a special context or scope.

Shutdown closes entry, signals stopping, drains accepted terminal and pipeline
work, then attempts every owned cleanup in reverse registration order. Repeated
calls share the shutdown operation. Failures are collected after all phases have
been attempted. An external owner must keep an opaque terminal available until
its explicit middleware lifetime has finished draining and cleaning up.

## Operation activities

PipelineOperation starts with one activity owned by EnqueueCore. After successful
lifetime entry it stores the owner directly, without a bound exit delegate.
Failure to enter must not invoke completion middleware whose resources may already
have been cleaned up, or decrement a lifetime it never entered.

Every invocation of an enqueue or execution continuation acquires an activity
before calling user code. Independently pending work retains its activity even
when middleware returns early or the caller-facing task times out. A continuation
cannot start after the operation's activity count reaches zero.

The execution envelope owns its root activity through execution and completion.
This avoids a separate root execution wrapper. Child invocations retain their own
activities; the root cannot substitute for abandoned or independent child work.

The final release runs quiescence callbacks before exiting the lifetime. Admission
capacity and linked cancellation sources are released there, after underlying work
has stopped. Pending-capacity callbacks instead release their reservation when the
delegate actually starts. Caller completion alone is not evidence of quiescence.

Completion is claimed once per operation. Enqueue failure and terminal execution
can race to report it; the winning outcome drives completion middleware. Caller
admission outcomes are recorded after completion middleware has processed failures
or recovery, rather than from the raw terminal outcome.

Each enqueue continuation uses async try/finally to retain its activity until its
own returned task settles, even if middleware independently completes the
caller-facing task earlier. This also preserves normal async exception semantics.

## Annotation-only forwarding

An annotation-only snapshot has no middleware or admission outcome to process.
Plain submissions can forward directly to the three sealed built-in flows, which
already reject closed entry and drain accepted delegates. Custom flows and opaque
terminals retain pipeline activity tracking; their completion hooks may not drain
all submitted work. Context-aware submissions still construct an operation so the
final annotations remain available to the delegate.

The successful forwarding path returns the terminal task directly. A cold catch
preserves pipeline submission's asynchronous failure behavior when a terminal
throws before returning a task. Its async failure helper also preserves the
canceled-task semantics of OperationCanceledException, including uncanceled tokens.

ExecutionContext flow, caller cancellation and terminal scheduler selection are
left to the existing scheduling paths. No lifetime scope needs to flow with them.