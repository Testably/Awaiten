# Getting started

This page opens a coffee shop from scratch. By the end you have a working container, you know how to resolve services, and you have seen the compiler catch a mistake.

## Install

Add the package to your project:

```sh
dotnet add package Awaiten
```

Awaiten targets `net10.0`, `net8.0`, and `netstandard2.0`, so it runs on modern .NET and on older toolchains. The package has no third-party dependencies.

## Declare a container

A container is a `static partial` class marked with `[Container]`. You register services with lifetime attributes on that class. Nothing else is needed. The source generator does the rest at build time.

```csharp
using Awaiten;

public sealed class EspressoMachine;
public sealed class Grinder;
public sealed class Cup(EspressoMachine machine, Grinder grinder);

[Container]
[Singleton<EspressoMachine>]
[Singleton<Grinder>]
[Transient<Cup>]
public static partial class CoffeeShop;
```

The generator emits a nested `Root` type for you. The `Root` is your composition root. It owns the singletons and it is the outermost scope.

## Create the root and resolve

Create the `Root`, then ask it for services with `Resolve<T>()`:

```csharp
using var shop = new CoffeeShop.Root();

Cup cup = shop.Resolve<Cup>();
```

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

using var shop = new CoffeeShop.Root();

using (IAwaitenScope order = shop.CreateScope())
{
    Order o = order.Resolve<Order>();
} // the order and everything it owns is disposed here
```

Inside one scope you get the same `Order` every time. A different scope gets a different one. See [Scopes](./lifetime/scopes) for the full picture.

## Let the compiler check your wiring

Forget to register a dependency and you do not find out at runtime. You find out when you build.

```csharp
public sealed class Cup(Grinder grinder);

[Container]
[Transient<Cup>]      // Grinder is never registered
public static partial class CoffeeShop;
```

> ```
> AWT101: 'Cup' needs 'Grinder', but nothing registers it
> ```

Cyclic graphs, captive dependencies, and async services touched synchronously are caught the same way. The full list is on the [Diagnostics](./diagnostics) page.

## Where to go next

- [Lifetimes](./registration/lifetimes) for singleton, scoped, and transient in depth.
- [Resolving services](./resolution/resolving-services) for the full resolve surface.
- [Async initialization](./async-initialization) once a service needs async setup.
