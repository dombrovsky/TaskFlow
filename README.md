# TaskFlow

**TaskFlow** is a high-performance, sequential task orchestration library for .NET. It provides thread-safe, controlled execution of asynchronous tasks with robust resource management, making it ideal for scenarios requiring serialized task execution, thread affinity, and clean disposal patterns.

[![NuGet](https://img.shields.io/nuget/v/TaskFlow.svg)](https://www.nuget.org/packages/TaskFlow/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

---

## Why TaskFlow?

TaskFlow is designed to address common challenges in asynchronous programming, such as:

- **Serialized Task Execution:** Ensures tasks are executed in the exact order they are enqueued.
- **Thread Affinity:** Supports execution on a dedicated thread, the current thread, or the thread pool.
- **Robust Disposal:** Guarantees all enqueued tasks complete before disposal finishes.
- **Cancellation Support:** Tasks are executed even when canceled, ensuring predictable execution order.
- **Synchronization Context Awareness:** Async/await inside enqueued delegates can capture and execute on the same `SynchronizationContext`.
- **Error Handling:** Provides rich error handling capabilities with custom exception handlers.
- **Dependency Injection Integration:** Seamlessly integrates with Microsoft.Extensions.DependencyInjection.

---

## Key Features

### 1. Sequential Task Execution
- Tasks are executed in the order they are enqueued, with no concurrency unless explicitly configured.
- Ideal for scenarios like database access, file operations, or API rate limiting.

### 2. Thread Affinity
- Run tasks on a dedicated thread, the current thread, or the thread pool.
- Useful for UI thread orchestration or isolating task execution.

### 3. Robust Resource Management
- `Dispose`/`DisposeAsync` ensures all tasks complete before releasing resources.
- Configurable timeouts for synchronous disposal.

### 4. Cancellation and Error Handling
- Tasks respect `CancellationToken` for cooperative cancellation.
- Register custom exception handlers for specific exception types or conditions.

### 5. Extensibility
- Extend `TaskFlowBase` to create custom task flow implementations.
- Use extensions like `TaskFlow.Extensions.Time` for throttling and scheduling.

---

## Getting Started

### Installation

Add the core package:
dotnet add package TaskFlow
For dependency injection support:
dotnet add package TaskFlow.Microsoft.Extensions.DependencyInjection
For time-based extensions (e.g., throttling):
dotnet add package TaskFlow.Extensions.Time
### Basic Usage
using var taskFlow = new TaskFlow();

// Enqueue tasks for sequential execution
var task1 = taskFlow.Enqueue(() => Console.WriteLine("Task 1"));
var task2 = taskFlow.Enqueue(async () => await Task.Delay(100));

await Task.WhenAll(task1, task2);
---

## Extensions

### Microsoft.Extensions.DependencyInjection

Integrate TaskFlow with your DI container:
services.AddTaskFlow();
### TaskFlow.Extensions.Time

Add throttling and scheduling capabilities:
var throttledScheduler = taskScheduler.Throttle(5, TimeSpan.FromSeconds(1));
---

## License

This library is licensed under the [MIT License](LICENSE).