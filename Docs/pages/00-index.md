---
title: Awaiten
sidebar_position: -1
---

# Awaiten [![Nuget](https://img.shields.io/nuget/v/Awaiten?logo=nuget)](https://www.nuget.org/packages/Awaiten)

<img align="right" width="200" src="https://raw.githubusercontent.com/Testably/Awaiten/main/Docs/Awaiten_256x256.png" alt="Awaiten logo" />

The async-first dependency injection container for .NET.

Awaiten is a Roslyn source generator. It wires your object graph at build time, so there is no runtime reflection and the generated code is plain, readable C#. Your container is native-AOT clean and trim-safe out of the box.

The configuration is verified by the compiler. Missing, cyclic, ambiguous, and lifetime-mismatched registrations are build errors, not surprises at startup. Awaiten ships around 70 diagnostics for this.

Its headline feature is **async initialization**. Some services need asynchronous setup after construction, like an espresso machine heating its boiler or a payment terminal connecting to the bank. Awaiten tracks those through the graph, and reaching one synchronously before it is ready is a compile error.

## A first shot

Think of the container as a coffee shop. The espresso machine is shared, so it is a singleton. Each order gets its own scope. A cup is made fresh every time, so it is transient.

```csharp
using Awaiten;

[Container]
[Singleton<EspressoMachine>]
[Scoped<Order>]
[Transient<Cup>]
public static partial class CoffeeShop;

await using var shop = new CoffeeShop.Root();
await shop.InitializeAsync();        // the espresso machine warms up

var cup = shop.Resolve<Cup>();       // a fresh cup, wired and ready
```

## Why Awaiten

| | Reflection-based containers | Awaiten |
|---|---|---|
| Wiring | At runtime, via reflection | At build time, as plain C# |
| Bad configuration | Fails at startup or first resolve | Fails at compile time |
| Async setup | Manual, easy to forget | Tracked, enforced by the compiler |
| Native AOT / trimming | Needs care, often breaks | Clean by construction |

## Getting started

1. [Install the package](./getting-started).
2. Declare a `[Container]` and register your services.
3. Create a `Root` and start resolving.

## Where to go next

- [Getting started](./getting-started) builds your first container end to end.
- [Principles & the composition root](./principles) is the design behind the mechanics; read it before the feature pages.
- [Lifetimes](./registration/lifetimes) explains singleton, scoped, and transient.
- [Async initialization](./async-initialization) covers the headline feature.
- [Diagnostics](./diagnostics) lists every compile-time check.
- [Complete example](./complete-example) assembles a full coffee shop.

## Packages

| Package | Description |
|---|---|
| `Awaiten` | The core: attributes, the source generator, and the runtime seams. No third-party dependencies. |
| `Awaiten.Extensions.DependencyInjection` | Microsoft.Extensions.DependencyInjection interop for ASP.NET Core, the generic host, and other containers. See the [MS.DI bridge](./msdi-bridge). |
