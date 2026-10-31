---
description: Install Awaiten, declare your first container, resolve services and let the compiler check your wiring.
---

import PackageInstall from '@site/src/components/PackageInstall';

# Getting started

This page opens a coffee shop from scratch. By the end you have a working container, you know how to resolve services, and you have seen the compiler catch a mistake.

:::note
Where an example checks a result in a test, it uses [aweXpect](/aweXpect/) in the form `await That(...)`. aweXpect is optional; Awaiten works with any test framework and assertion library.
:::

## Requirements

:::info[.NET 10.0.300 SDK and C# 11]
- Build with the .NET SDK 10.0.300 or later. The source generator needs the C# compiler that ships with it.
- Write C# 11 or later. The registration attributes in these docs, such as `[Singleton<T>]`, are generic attributes.
- Target `net10.0`, `net8.0` or any framework that supports `netstandard2.0`, including .NET Framework 4.8. The package has no third-party dependencies; on netstandard2.0 it references only `Microsoft.Bcl.AsyncInterfaces`, for async disposal.
:::

## Install the package

<PackageInstall package="Awaiten" />

| Package | Description |
|---|---|
| `Awaiten` | The core: attributes, the source generator, and the runtime seams. No third-party dependencies; on netstandard2.0 only `Microsoft.Bcl.AsyncInterfaces`. |
| `Awaiten.Extensions.DependencyInjection` | Microsoft.Extensions.DependencyInjection interop for ASP.NET Core, the generic host, and other containers. See the [MS.DI bridge](./07-hosting/01-msdi-bridge.md). |

## Build your first container

In short: declare a `[Container]`, register your services on it, then create its `Root` and start resolving.

A container is a `static partial` class marked with `[Container]`. You register services with lifetime attributes on that class. Nothing else is needed. The source generator does the rest at build time.

```csharp title="Program.cs"
using Awaiten;

using CoffeeShop.Root shop = new();

Cup cup = shop.Resolve<Cup>();

public sealed class EspressoMachine;
public sealed class Grinder;
public sealed class Cup(EspressoMachine machine, Grinder grinder);

[Container]
[Singleton<EspressoMachine>]
[Singleton<Grinder>]
[Transient<Cup>]
public static partial class CoffeeShop;
```

The generator emits a nested `Root` type for you. The `Root` is your composition root. It owns the singletons and it is the outermost scope. You create it, then ask it for services with `Resolve<T>()`.

Awaiten built the `Cup` for you and passed in the shared `EspressoMachine` and `Grinder`. You wrote no factory code.

Dispose the `Root` when you are done. It disposes everything it owns. Use `await using` if any of your services are async-disposable.

## Register against an interface

Most services hide behind an interface. Pass the implementation first, then the service type:

```csharp
public interface IBrewer;
public sealed class EspressoBrewer : IBrewer;

[Container]
[Singleton<EspressoBrewer, IBrewer>]
public static partial class CoffeeShop;

// resolve by the interface:
IBrewer brewer = shop.Resolve<IBrewer>();
```

## Open a scope per order

Some things belong to a single order, like the running tab. Register them as `[Scoped]` and open a scope for each order:

```csharp
[Container]
[Scoped<Order>]
public static partial class CoffeeShop;

using CoffeeShop.Root shop = new();

using (IAwaitenScope order = shop.CreateScope())
{
    Order o = order.Resolve<Order>();
} // the order and everything it owns is disposed here
```

Inside one scope you get the same `Order` every time. A different scope gets a different one. See [Scopes](./03-core-concepts/02-scopes.md) for the full picture.

## Let the compiler check your wiring

Forget to register a dependency and you do not find out at runtime. You find out when you build.

```csharp expect=AWT101
public sealed class Cup(Grinder grinder);

[Container]
[Transient<Cup>]      // Grinder is never registered
public static partial class CoffeeShop;
```

```text title="Build error"
error AWT101: 'Cup' cannot be resolved: 'Cup' requires 'Grinder', which is not registered on the container
```

Cyclic graphs, captive dependencies, and async services touched synchronously are caught the same way. The full list is on the [Diagnostics](./09-diagnostics.md) page.

## Next steps

- [Complete example](./02-complete-example.md) for a full container that uses the main features together.
- [Lifetimes](./03-core-concepts/01-lifetimes.md) for singleton, scoped, and transient in depth.
- [Resolving services](./05-resolution/01-resolving-services.md) for the full resolve surface.
- [Async initialization](./03-core-concepts/03-async-initialization.md) once a service needs async setup.
