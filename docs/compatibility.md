---
layout: page
title: Compatibility
permalink: /compatibility/
---

# Compatibility

## Package and framework matrix

| Package | Target frameworks | Purpose |
|---|---|---|
| `TaskFlow` | `netstandard2.0`, `net8.0`, `net10.0` | Core flows, scheduler contracts, cancellation, timeout, error observation, annotations, and interception |
| `TaskFlow.Extensions.Time` | `netstandard2.0` | `WithThrottle` for older targets using `Microsoft.Bcl.TimeProvider` |
| `TaskFlow.Microsoft.Extensions.DependencyInjection` | `netstandard2.0`, `net8.0`, `net10.0` | Scoped and named flow registration and factories |
| `TaskFlow.Microsoft.Extensions.Logging` | `netstandard2.0`, `net8.0`, `net10.0` | Structured operation lifecycle logging |

The core package includes `WithThrottle` when targeting .NET 8 or .NET 10. Its `netstandard2.0` asset omits that source because `TimeProvider` is not part of the target framework. Install `TaskFlow.Extensions.Time` in a `netstandard2.0` consumer to receive the same public API through `Microsoft.Bcl.TimeProvider`.

## Language and runtime use

The packages can be consumed from compatible target frameworks regardless of the repository's build SDK. Repository builds currently use the .NET 10 SDK, and the test projects execute against .NET 8 and .NET 10.

`IAsyncDisposable` support for the `netstandard2.0` asset is supplied through `Microsoft.Bcl.AsyncInterfaces`. Prefer `await using` where the consuming language and runtime support it.

## Package selection examples

For a .NET 8 or .NET 10 application:

```shell
dotnet add package TaskFlow
```

For a library targeting `netstandard2.0` that needs throttling:

```shell
dotnet add package TaskFlow
dotnet add package TaskFlow.Extensions.Time
```

Add integration packages only when needed:

```shell
dotnet add package TaskFlow.Microsoft.Extensions.DependencyInjection
dotnet add package TaskFlow.Microsoft.Extensions.Logging
```

All TaskFlow APIs use the `System.Threading.Tasks.Flow` namespace. Logging configuration also uses `Microsoft.Extensions.Logging`, and registration uses `Microsoft.Extensions.DependencyInjection`.
