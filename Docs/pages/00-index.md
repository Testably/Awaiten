---
description: Awaiten is an async-first, compile-time dependency injection container for .NET that wires your object graph with a source generator.
---

# Awaiten

<img align="right" width="200" src="https://raw.githubusercontent.com/Testably/Awaiten/main/Docs/Awaiten_256x256.png" alt="Awaiten logo" />

The async-first dependency injection container for .NET. Awaiten wires your object graph at build time and checks it with the compiler, including services that need asynchronous setup.

Think of the container as a coffee shop. The espresso machine is shared, so it is a singleton. Each order gets its own scope. A cup is made fresh every time, so it is transient.

```csharp
using Awaiten;

[Container]
[Singleton<EspressoMachine>]
[Scoped<Order>]
[Transient<Cup>]
public static partial class CoffeeShop;

await using CoffeeShop.Root shop = new();
await shop.InitializeAsync();        // the espresso machine warms up

Cup cup = shop.Resolve<Cup>();       // a fresh cup, wired and ready
```

## Why Awaiten?

Awaiten is a Roslyn source generator. It wires your object graph at build time, so there is no runtime reflection and the generated code is plain, readable C#. Your container is native-AOT clean and trim-safe out of the box.

The configuration is verified by the compiler. Missing, cyclic, ambiguous, and lifetime-mismatched registrations are build errors, not surprises at startup. Awaiten ships more than 100 diagnostics for this.

Its headline feature is **async initialization**. Some services need asynchronous setup after construction, like an espresso machine heating its boiler or a payment terminal connecting to the bank. Awaiten tracks those through the graph, and reaching one synchronously before it is ready is a compile error.

| | Reflection-based containers | Awaiten |
|---|---|---|
| Wiring | At runtime, via reflection | At build time, as plain C# |
| Bad configuration | Fails at startup or first resolve | Fails at compile time |
| Async setup | Manual, easy to forget | Tracked, enforced by the compiler |
| Native AOT / trimming | Needs care, often breaks | Clean by construction |

## Where to go next

- [Getting started](./01-getting-started.md) builds your first container end to end.
- [Complete example](./02-complete-example.md) assembles a full coffee shop.
- [Lifetimes](./03-core-concepts/01-lifetimes.md) explains singleton, scoped, and transient.
- [Async initialization](./03-core-concepts/03-async-initialization.md) covers the headline feature.
- [MS.DI bridge](./07-hosting/01-msdi-bridge.md) runs a container under ASP.NET Core and the generic host.
- [Design principles & the composition root](./08-design-principles.md) is the design behind the mechanics; read it once you know the basics.
- [Diagnostics](./09-diagnostics.md) lists every compile-time check.
- [Benchmarks](./10-benchmarks.mdx) compares Awaiten with other .NET containers.
