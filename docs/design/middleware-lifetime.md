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

PipelineOperation starts with one activity. Plain Enqueue enters its lifetime
synchronously to protect its own middleware resources before forwarding. It does
not probe the terminal scheduler's disposal state. Entry and closure use the same
lock: accepted work is counted before shutdown can drain it. After successful entry, the operation stores its
owner directly and EnqueueCore owns the initial activity once forwarding returns
a task. If entry or synchronous forwarding fails first, submission releases the
activity and propagates the exception without invoking completion middleware.
A failed entry never exits a lifetime it did not enter. Explicit admission Submit retains its
separate asynchronous decision/completion contract.

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

Each enqueue continuation forwards synchronously before observing the returned
task. Immediate scheduling rejection releases its activity and propagates directly
through forwarding pipelines, preserving the original exception. Once a task is
returned, observation retains the activity until that task settles, even when
middleware completes the caller-facing task earlier. Returned task failures enter
completion middleware; they are never promoted into synchronous exceptions.

Admission middleware can short-circuit or defer forwarding according to its own
state. It does not inspect terminal disposal to manufacture an early rejection.
Transparent wrappers preserve exceptions from synchronous forwarding. Middleware
that owns asynchronous admission or scheduling, including timeout and queuing,
may report forwarding failures through its returned task even if it invokes the
continuation immediately. Such task failures still enter completion middleware.
Explicit Submit continues to report admission and scheduling outcomes through
its decision/completion tasks.

Cancellation scope and cancel-previous wrappers separate forwarding from task
observation so synchronous rejection disposes their linked tokens immediately,
while accepted operations retain those resources until their tasks settle.
Timeout retains its competing-task semantics and does not require a synchronous
startup path in the general-purpose task-racing helper.

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