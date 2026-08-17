---
layout: page
title: Choosing TaskFlow
permalink: /choosing-taskflow/
---

# Choosing TaskFlow

Use this page when you are deciding between TaskFlow and common .NET primitives such as `lock`, `SemaphoreSlim`, `Channel<T>`, or custom task chaining.

TaskFlow is an owned FIFO execution lane for submitted operations. Each submission gets its own returned task while the lane controls serialization and lifetime.

## Comparison at a glance

| Option | Best fit | What it does not give by default |
|---|---|---|
| `lock` | Very short synchronous critical sections | No `await` in the critical section, no per-submission task model, no queue ownership |
| `SemaphoreSlim` | Async mutual exclusion around one code path | You build ordering/lifecycle/error-observation conventions yourself |
| `Channel<T>` | Producer-consumer data exchange with buffering/backpressure | No per-caller operation task unless you add correlation and completion plumbing |
| Custom task chaining | Full control with explicit tradeoffs | Easy to regress ordering/cancellation/disposal semantics over time |
| TaskFlow | Owned sequential operation lane with per-call tasks and composable policies | Not a parallel work queue and not hard preemption |

## Use `lock` when

- The work is synchronous and short.
- You only need in-process mutual exclusion.
- You do not need per-caller asynchronous completion tasks.

Use TaskFlow instead when callers are asynchronous and you must serialize submitted operations without blocking threads.

## Use `SemaphoreSlim` when

- You simply need async mutual exclusion around a specific operation.
- You can define and maintain your own conventions for lifetime and shutdown.
- You do not need a first-class lane abstraction.

Use TaskFlow instead when you need lane ownership, consistent per-submission outcomes, and policy composition (`WithTimeout`, `OnError`, cancellation scopes, latest-request-wins).

## Use `Channel<T>` when

- You have producers and consumers exchanging data.
- Buffering, bounded capacity, and backpressure are the central design concerns.
- Consumers can pull and process messages independently.

Use TaskFlow instead when callers submit operations and each call must receive an individual task representing that operation's outcome.

## Use custom task chaining when

- You have niche scheduling semantics and accept implementation/maintenance cost.
- Existing primitives do not fit your constraints.

Use TaskFlow instead when you want established sequential-lane semantics without repeatedly rebuilding ordering, cancellation, disposal, and failure behavior.

## Tradeoffs you accept with TaskFlow

- Cancellation is cooperative. Delegates must observe the provided token.
- Built-in flows invoke accepted queued delegates even if already canceled, so the lane progresses deterministically.
- One lane is intentionally sequential, so long operations can create head-of-line blocking.

These are behavior contracts, not incidental implementation details. See [Semantics and pitfalls](semantics-and-pitfalls.md) for details.

## Typical scenarios where TaskFlow is a strong fit

- Serialize access to a non-thread-safe resource while preserving async caller APIs.
- Convert synchronous callbacks into ordered asynchronous processing.
- Implement latest-request-wins behavior with cooperative cancellation.
- Bind operation processing to a component or dependency-injection scope lifetime.

See concrete examples in [Recipes](recipes.md).

## Related guides

- [Getting started](getting-started.md)
- [Concepts and lifecycle](concepts-and-lifecycle.md)
- [Execution models](execution-models.md)
- [Semantics and pitfalls](semantics-and-pitfalls.md)
- [Extensions](extensions/index.md)
- [Dependency injection](dependency-injection.md)
