# TaskFlow

**TaskFlow** is a robust, high-performance, extensible, and thread-safe library for orchestrating and controlling the execution of asynchronous tasks in .NET. It provides advanced patterns for sequential task execution, resource management, and cancellation, making it ideal for scenarios where you need more than just `SemaphoreSlim` or basic Task chaining.

[![NuGet](https://img.shields.io/nuget/v/TaskFlow.svg)](https://www.nuget.org/packages/TaskFlow/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

---

## Key Features

- **Sequential Task Execution:** Guarantee that tasks are executed in the order they are enqueued, with no concurrency unless explicitly configured.
- **Thread Affinity:** Run tasks on a dedicated thread, the current thread, or the thread pool, with full control over execution context.
- **Robust Disposal:** Dispose/DisposeAsync will only complete after all enqueued tasks have finished, ensuring clean shutdowns. This makes it ideal for managing fire-and-forget tasks by binding their lifetime to a specific scope.
- **Cancellation Support:** All enqueued task functions are executed, even if canceled before execution, ensuring predictable execution order.
- **SynchronizationContext Awareness:** Async/await inside enqueued delegates will execute continuations on the same `TaskFlow` if a `SynchronizationContext` is captured.
- **Extensibility:** Extend `TaskFlowBase` to create custom task flow implementations or use extension methods and wrappers to enhance functionality, such as throttling, error handling, or scoped cancellation.
- **Clean Task Pipeline Definition:** Define task pipelines separately from execution logic using extension methods from `System.Threading.Tasks.Flow.Extensions`, enabling better segregation of responsibilities and cleaner code.
- **Dependency Injection Integration:** Extensions for `Microsoft.Extensions.DependencyInjection` for easy registration and scoping.

---

## When Should You Use TaskFlow?

TaskFlow is ideal for scenarios where you need:

- **Serialized access to a resource** (e.g., database, file, hardware) from multiple async operations.
- **Order-preserving task execution** (e.g., message processing, event handling).
- **Thread affinity** (e.g., UI thread, dedicated worker thread).
- **Graceful shutdown** with guaranteed completion of all in-flight work.
- **Advanced error handling and cancellation patterns.**
- **Fire-and-forget task lifetime management:** Bind fire-and-forget operations to a scope by disposing the `TaskFlow` instance, ensuring proper cleanup and resource management.
- **Segregation of responsibilities:** Use extension methods to define task pipelines separately from execution logic, improving maintainability and readability.

---

## Getting Started

### Installation

Add the core package:
`dotnet add package TaskFlow`

For dependency injection support:
`dotnet add package TaskFlow.Microsoft.Extensions.DependencyInjection`
### Basic Usage
```csharp
using var taskFlow = new TaskFlow();

// Enqueue tasks for sequential execution
var task1 = taskFlow.Enqueue(() => Console.WriteLine("Task 1"));
var task2 = taskFlow.Enqueue(async () => await Task.Delay(100));
```
---

## Extensions

## License

This library is licensed under the [MIT License](LICENSE).