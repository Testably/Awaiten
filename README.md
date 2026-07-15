# Awaiten

[![Nuget](https://img.shields.io/nuget/v/Awaiten)](https://www.nuget.org/packages/Awaiten)
[![Build](https://github.com/Testably/Awaiten/actions/workflows/build.yml/badge.svg)](https://github.com/Testably/Awaiten/actions/workflows/build.yml)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=Testably_Awaiten&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=Testably_Awaiten)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=Testably_Awaiten&metric=coverage)](https://sonarcloud.io/summary/overall?id=Testably_Awaiten)

**The async-first dependency injection container for .NET.**

Awaiten is a Roslyn source generator that wires your object graph at build time. There is no runtime reflection, the generated code is plain readable C#, and your container is native-AOT clean. The configuration is verified by the compiler: missing, cyclic, ambiguous, and lifetime-mismatched registrations are build errors.

Its headline feature is **async initialization**. Services that need asynchronous setup after construction, like opening a connection or handshaking with hardware, are tracked through the graph. Reaching one before it is ready is a compile error.

## Quick start

```csharp
using Awaiten;

[Container]
[Singleton<EspressoMachine>]
[Scoped<Order>]
[Transient<Cup>]
public static partial class CoffeeShop;

await using var shop = new CoffeeShop.Root();
await shop.InitializeAsync();        // async-initialized services are warmed up
var cup = shop.Resolve<Cup>();
```

Install with:

```sh
dotnet add package Awaiten
```

## Features

- **Async initialization.** `IAsyncInitializable` services are initialized in dependency order, and reaching one synchronously is a compile error.
- **Compile-time safety.** Around 90 diagnostics turn wiring mistakes into build errors instead of startup crashes.
- **No reflection.** The generated code is plain C#, so the container is native-AOT clean and trim-safe.
- **A full toolbox.** Lifetimes, scopes, keyed services, decorators, composites, open generics, collections, factories, modules, assembly scanning, property injection, and `Owned<T>` for disposable transients.
- **MS.DI interop.** A separate package bridges into Microsoft.Extensions.DependencyInjection for ASP.NET Core and the generic host.

## Packages

| Package | Description |
|---|---|
| `Awaiten` | The core: attributes, the source generator, and the runtime seams. No third-party dependencies. |
| `Awaiten.Extensions.DependencyInjection` | Microsoft.Extensions.DependencyInjection interop (ASP.NET Core, generic host, and other containers). |

## Documentation

Full documentation lives at [docs.testably.org/Awaiten](https://docs.testably.org/Awaiten).

- [Getting started](https://docs.testably.org/Awaiten/getting-started)
- [Design principles & the composition root](https://docs.testably.org/Awaiten/design-principles)
- Registration: [lifetimes](https://docs.testably.org/Awaiten/registration/lifetimes), [factories and instances](https://docs.testably.org/Awaiten/registration/factories-and-instances), [decorators](https://docs.testably.org/Awaiten/registration/decorators), [composites](https://docs.testably.org/Awaiten/registration/composites), [keyed services](https://docs.testably.org/Awaiten/registration/keyed-services), [open generics](https://docs.testably.org/Awaiten/registration/open-generics), [scanning](https://docs.testably.org/Awaiten/registration/scanning), [modules](https://docs.testably.org/Awaiten/registration/modules)
- Resolution: [resolving services](https://docs.testably.org/Awaiten/resolution/resolving-services), [relationships](https://docs.testably.org/Awaiten/resolution/relationships), [collections](https://docs.testably.org/Awaiten/resolution/collections), [keyed dictionaries](https://docs.testably.org/Awaiten/resolution/keyed-dictionaries), [property injection](https://docs.testably.org/Awaiten/resolution/property-injection), [runtime arguments](https://docs.testably.org/Awaiten/resolution/runtime-arguments), [context-aware factories](https://docs.testably.org/Awaiten/resolution/context-aware-factories)
- [Async initialization](https://docs.testably.org/Awaiten/async-initialization)
- Lifetime and disposal: [scopes](https://docs.testably.org/Awaiten/lifetime/scopes), [disposal](https://docs.testably.org/Awaiten/lifetime/disposal), [owned](https://docs.testably.org/Awaiten/lifetime/owned), [lifetime safety](https://docs.testably.org/Awaiten/lifetime/lifetime-safety), [lifecycle hooks](https://docs.testably.org/Awaiten/lifetime/lifecycle-hooks)
- Advanced: [external services](https://docs.testably.org/Awaiten/advanced/external-services), [generic variance](https://docs.testably.org/Awaiten/advanced/generic-variance)
- [MS.DI bridge](https://docs.testably.org/Awaiten/msdi-bridge)
- [Diagnostics](https://docs.testably.org/Awaiten/diagnostics)
- [Complete example](https://docs.testably.org/Awaiten/complete-example)
