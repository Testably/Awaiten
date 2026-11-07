# Awaiten

[![Nuget](https://img.shields.io/nuget/v/Awaiten)](https://www.nuget.org/packages/Awaiten)
[![Build](https://github.com/awaiten-dev/Awaiten/actions/workflows/build.yml/badge.svg)](https://github.com/awaiten-dev/Awaiten/actions/workflows/build.yml)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=awaiten-dev_Awaiten&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=awaiten-dev_Awaiten)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=awaiten-dev_Awaiten&metric=coverage)](https://sonarcloud.io/summary/overall?id=awaiten-dev_Awaiten)

**The async-first dependency injection container for .NET.**

Awaiten is a Roslyn source generator that wires your object graph at build time. There is no runtime reflection, the generated code is plain readable C#, and your container is native-AOT clean. The configuration is verified by the compiler: missing, cyclic, ambiguous, and lifetime-mismatched registrations are build errors.

Its headline feature is **async initialization**. Services that need asynchronous setup after construction, like opening a connection or handshaking with hardware, are tracked through the graph. Reaching one before it is ready is a compile error.

## Installation

```shell
dotnet add package Awaiten
```

## Quick start

```csharp
using Awaiten;

public interface IWarmUp                                  // your own interface, no Awaiten reference
{
    Task WarmUpAsync(CancellationToken cancellationToken);
}

public sealed class EspressoMachine : IWarmUp
{
    public Task WarmUpAsync(CancellationToken cancellationToken) => HeatBoilerAsync(cancellationToken);
}

[Container]
[AsyncInitializer<IWarmUp>(nameof(IWarmUp.WarmUpAsync))]   // every IWarmUp is warmed up before use
[Singleton<EspressoMachine>]
[Scoped<Order>]
[Transient<Cup>]
public static partial class CoffeeShop;

await using CoffeeShop.Root shop = new();
await shop.InitializeAsync();        // the espresso machine warms up
Cup cup = shop.Resolve<Cup>();
```

## Features

- **Async initialization.** Services named by an async initializer convention or an asynchronous `OnActivated` hook are initialized in dependency order, and reaching one synchronously is a compile error.
- **Compile-time safety.** More than 100 diagnostics turn wiring mistakes into build errors instead of startup crashes.
- **No reflection.** The generated code is plain C#, so the container is native-AOT clean and trim-safe.
- **A full toolbox.** Lifetimes, scopes, keyed services, decorators, composites, open generics, collections, factories, modules, assembly scanning, property injection, and `Owned<T>` for disposable transients.
- **MS.DI interop.** A separate package bridges into Microsoft.Extensions.DependencyInjection for ASP.NET Core and the generic host.

## Packages

| Package | Description |
|---|---|
| `Awaiten` | The core: attributes, the source generator, and the runtime seams. No third-party dependencies; on netstandard2.0 only `Microsoft.Bcl.AsyncInterfaces`. |
| `Awaiten.Extensions.DependencyInjection` | Microsoft.Extensions.DependencyInjection interop (ASP.NET Core, generic host, and other containers). |

## Documentation

Full documentation lives at [awaiten-dev.github.io/Awaiten](https://awaiten-dev.github.io/Awaiten).

- [Getting started](https://awaiten-dev.github.io/Awaiten/docs/getting-started)
- [Complete example](https://awaiten-dev.github.io/Awaiten/docs/complete-example)
- Core concepts: [lifetimes](https://awaiten-dev.github.io/Awaiten/docs/core-concepts/lifetimes), [scopes](https://awaiten-dev.github.io/Awaiten/docs/core-concepts/scopes), [async initialization](https://awaiten-dev.github.io/Awaiten/docs/core-concepts/async-initialization), [disposal](https://awaiten-dev.github.io/Awaiten/docs/core-concepts/disposal)
- Registration: [factories and instances](https://awaiten-dev.github.io/Awaiten/docs/registration/factories-and-instances), [decorators](https://awaiten-dev.github.io/Awaiten/docs/registration/decorators), [composites](https://awaiten-dev.github.io/Awaiten/docs/registration/composites), [keyed services](https://awaiten-dev.github.io/Awaiten/docs/registration/keyed-services), [open generics](https://awaiten-dev.github.io/Awaiten/docs/registration/open-generics), [scanning](https://awaiten-dev.github.io/Awaiten/docs/registration/scanning), [modules](https://awaiten-dev.github.io/Awaiten/docs/registration/modules)
- Resolution: [resolving services](https://awaiten-dev.github.io/Awaiten/docs/resolution/resolving-services), [relationships](https://awaiten-dev.github.io/Awaiten/docs/resolution/relationships), [collections](https://awaiten-dev.github.io/Awaiten/docs/resolution/collections), [keyed dictionaries](https://awaiten-dev.github.io/Awaiten/docs/resolution/keyed-dictionaries), [property injection](https://awaiten-dev.github.io/Awaiten/docs/resolution/property-injection), [runtime arguments](https://awaiten-dev.github.io/Awaiten/docs/resolution/runtime-arguments), [context-aware factories](https://awaiten-dev.github.io/Awaiten/docs/resolution/context-aware-factories), [generic variance](https://awaiten-dev.github.io/Awaiten/docs/resolution/generic-variance)
- Ownership and lifecycle: [owned](https://awaiten-dev.github.io/Awaiten/docs/lifetime/owned), [lifetime safety](https://awaiten-dev.github.io/Awaiten/docs/lifetime/lifetime-safety), [lifecycle hooks](https://awaiten-dev.github.io/Awaiten/docs/lifetime/lifecycle-hooks)
- Hosting: [MS.DI bridge](https://awaiten-dev.github.io/Awaiten/docs/hosting/msdi-bridge), [external services](https://awaiten-dev.github.io/Awaiten/docs/hosting/external-services)
- [Design principles & the composition root](https://awaiten-dev.github.io/Awaiten/docs/design-principles)
- [Diagnostics](https://awaiten-dev.github.io/Awaiten/docs/diagnostics)
