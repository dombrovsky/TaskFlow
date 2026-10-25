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

## Structured middleware contract

Before a middleware invocation's returned Task/ValueTask settles, every downstream
continuation it started must have settled. Return `next(context)` directly, await it
and do additional work, or short-circuit without invoking it. Execution middleware
may retry sequentially or start concurrent attempts provided it awaits **every**
started attempt before returning, throwing, or canceling. `Task.WhenAll` is suitable
for joining concurrent attempts, including when one fails.

Starting downstream work and returning an independent result while it still runs,
retaining a continuation for use after its middleware finishes, and detached
execution or completion processing are unsupported. This is a usage requirement;
the pipeline does not add per-layer observers or counters to detect every violation.
Existing enqueue at-most-once validation, operation-context identity validation,
annotation visibility, local state and cancellation-token replacement remain.

## Operation ownership

PipelineOperation begins with one ownership reference. Plain Enqueue enters its
lifetime synchronously before forwarding. Entry and closure use the same lock, so
accepted work is counted before shutdown drains it. Synchronous entry/forwarding
failure releases ownership and propagates the original exception without invoking
completion middleware. Explicit admission Submit retains its separate asynchronous
decision/completion contract; TrySubmit retains atomic reservations and synchronous
accept/full/closed decisions and its existing supported-pipeline restrictions.

The enqueue root retains ownership through admission waiting, queuing and its
returned task. One execution envelope retains ownership through the whole structured
execution/completion tree. Ordinary continuation calls forward directly, preserving
synchronous rejection and scheduler/context placement. They neither retain an
ownership reference nor allocate an asynchronous observer. Middleware must join
its children, including completion transformations and failure recovery.

Completion is claimed once. Enqueue failure and terminal execution can race to
report it; the winner drives completion middleware. Caller admission outcomes are
recorded after completion middleware has processed failures or recovery.

Final ownership release runs quiescence callbacks before exiting the lifetime.
Outstanding capacity and linked cancellation resources remain owned until all work
settles. Pending capacity releases only when the user delegate actually starts.
These rules also cover canceled queued operations, asynchronous admission, owner
shutdown, and completion processing after cancellation notification.

## Explicit caller-wait boundary and timeout compatibility

`TaskSchedulerEnqueueContext<TResult>.InvokeWithCallerWait(next, callerWait)`
transfers producer ownership to the operation. The wait policy receives an owned
enqueue continuation; invoke it at most once before the policy finishes. Its task
represents actual downstream termination. The policy may return an independent
caller wait task, while the operation retains the producer until it actually settles.
This is an explicit operation-ownership boundary, not a middleware type exception.
It is available to custom policies, works after asynchronous admission, and nests.
Only this boundary needs an extra producer observer/reference.

`WithTimeout`, including the TimeProvider/netstandard implementation, uses this
boundary. Queue/admission waiting still consumes the timeout budget. Caller timeout
still requests cooperative cancellation and can finish before a delegate that
ignores cancellation; it does not prove producer termination, release outstanding
capacity, permit overlapping same-key execution, or allow owned middleware cleanup.
The winning completion outcome and completion middleware behavior are unchanged.
Transparent wrappers still preserve synchronous forwarding rejection; timeout and
other async wrappers continue reporting forwarding failures through their tasks.

Custom enqueue policies that previously returned early after directly starting
`next` must migrate to this boundary or await `next` before finishing. Execution
and completion policies must join all attempts; no detached counterpart is provided.
Direct forwarding now exposes the original downstream task status to middleware.
For example, a terminal returning `Task.FromException<T>(OperationCanceledException)`
is observed as Faulted by its immediate enqueue caller, rather than being normalized
to Canceled by an async per-layer observer. Await still throws the same exception;
the outer scheduled task retains its cancellation behavior. Execution continuation
invocation can now throw synchronously instead of returning a faulted ValueTask.
Protect both invocation and awaiting with the same try/catch. Concurrent-attempt
orchestration should use an async attempt helper to convert synchronous throws to
faulted tasks before joining all attempts, as shown in the customization example.

Cancellation scopes and cancel-previous already await their continuation and keep
their existing linked-token scopes. Callers still dispose the external owner.

## Future policies

Issue #10 can register a key before forwarding, while T2/T3 short-circuit and await
a shared result. A promoted follower retains its own context, delegate, state and
token, invokes its own enqueue continuation at most once while its invocation is
still pending, and awaits it. Canceling T1's caller does not settle T2/T3. If T1 is
executing, its actual termination must precede T2's execution; a caller-wait boundary
can separate that notification from producer termination. Normal faults are shared,
not implicit promotion. Closing the owner prevents promotion and settles waiters.
This PR does not implement promotion, queue-position inheritance, or single-flight.

Operation ownership before forwarding also permits component-local scopes sharing
an execution lane (#13) and future debounce timers/pending submissions (#21).
Atomic admission and pending/outstanding budgets (#37) remain separate from caller
wait outcomes. These policies must own and settle their waiters on shutdown rather
than detaching them from their operation lifetime.

## Annotation-only forwarding

An annotation-only snapshot has no middleware or admission outcome to process.
Plain submissions can forward directly to the three sealed built-in flows, which
already reject closed entry and drain accepted delegates. Custom flows and opaque
terminals retain pipeline activity tracking; their completion hooks may not drain
all submitted work. Context-aware submissions still construct an operation so the
final annotations remain available to the delegate.

Forwarding returns the terminal task directly. Enqueue-time validation and
scheduler rejection propagate synchronously, including ObjectDisposedException
when the terminal has closed entry. Failures and cancellation from accepted
operations remain observable through their returned tasks.

ExecutionContext flow, caller cancellation and terminal scheduler selection are
left to the existing scheduling paths. No lifetime scope needs to flow with them.

## Synchronous admission

TrySubmit reserves capacity in the existing admission queues. A single drain owner
covers both a queued worker and active dispatch. Ownership and idle publication
use the queue lock, preventing duplicate drains and lost wakeups. Operation,
middleware, and terminal dispatch callbacks run outside that lock and off the
synchronous producer's stack.

Deferred admissions capture context before the producer returns; a legacy drain
may later hand off to a worker at TrySubmit. The uncoordinated gate skips capture
for legacy work that owns inline dispatch. Legacy-only bounded waiters retain the
capacity releaser's context; coordinated submissions retain their producer context.
Operation activities own execution and completion regardless of context flow.

Operation, middleware, and terminal scheduling failures settle affected completions
without abandoning the drain. Runtime thread-pool queue failure can occur after
callback publication, so resetting ownership and retrying could dispatch twice.
There is no inline fallback; progress requires a functioning thread pool and
cooperative work.