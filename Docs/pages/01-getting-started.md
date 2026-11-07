---
description: Install Awaiten, build your first container, add a service that warms up asynchronously and let the compiler check your wiring.
---

import PackageInstall from '@site/src/components/PackageInstall';

# Getting started

Awaiten is the async-first dependency injection container for .NET. It wires your object graph at build time and checks it with the compiler, including services that need asynchronous setup.

This page opens a coffee shop from scratch. By the end you have a working container, you have added an espresso machine that warms up before it serves, and you have seen the compiler catch a mistake.

## Requirements

:::info[.NET SDK 10.0.300 and C# 11]
- Build with the .NET SDK 10.0.300 or later. The source generator needs the C# compiler that ships with it.
- Write C# 11 or later. The registration attributes, such as `[Singleton<T>]`, are generic attributes.
- Target `net10.0`, `net8.0` or any framework that supports `netstandard2.0`, including .NET Framework 4.8.
:::

:::tip[.NET Framework and netstandard2.0 projects]
Projects that target .NET Framework or netstandard2.0 default to C# 7.3. Raise the language version in the project file:

```xml
<LangVersion>latest</LangVersion>
```
:::

## Install the package

<PackageInstall package="Awaiten" />

`Awaiten` is all you need: the attributes, the source generator and the runtime types. It has no third-party dependencies; on netstandard2.0 it references only `Microsoft.Bcl.AsyncInterfaces`, for async disposal. When you host the container in ASP.NET Core or the generic host, add `Awaiten.Extensions.DependencyInjection` as well, as the [MS.DI bridge](./07-hosting/01-msdi-bridge.md) describes.

## Build your first container

A container is a `static partial` class marked with `[Container]`. You register services on it with lifetime attributes, and the source generator writes the wiring at build time.

```csharp title="Program.cs"
using Awaiten;

using CoffeeShop.Root shop = new();

Cup cup = shop.Resolve<Cup>();
Console.WriteLine($"Your cup holds {cup.Contents}.");

public sealed class Grinder
{
    public string Grind() => "fresh grounds";
}

public sealed class Cup(Grinder grinder)
{
    public string Contents => grinder.Grind();
}

[Container]
[Singleton<Grinder>]
[Transient<Cup>]
public static partial class CoffeeShop;
```

```text title="Output"
Your cup holds fresh grounds.
```

The generator emits a nested `Root` type. The `Root` is your composition root: it owns the singletons and it is the outermost scope. You create it, then ask it for services.

Awaiten built the `Cup` and passed in the shared `Grinder`. You wrote no factory code. A singleton is created once and shared, a transient is made fresh for every resolve.

Dispose the `Root` when you are done. It disposes everything it owns.

## Add a service that warms up

Some services need asynchronous setup before they can work. The espresso machine has to heat its boiler before it pulls a shot. Add it to the shop and tell the container how it warms up:

```csharp title="Program.cs"
using Awaiten;

await using CoffeeShop.Root shop = new();

// highlight-next-line
Cup cup = await shop.ResolveAsync<Cup>();   // the espresso machine warms up first
Console.WriteLine($"Your cup holds {cup.Contents}.");

public interface IWarmUp
{
    Task WarmUpAsync(CancellationToken cancellationToken);
}

public sealed class EspressoMachine : IWarmUp
{
    public Task WarmUpAsync(CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);   // stands in for heating the boiler

    public string PullShot() => "an espresso shot";
}

public sealed class Cup(EspressoMachine machine, Grinder grinder)
{
    public string Contents => $"{machine.PullShot()} from {grinder.Grind()}";
}

[Container]
// highlight-start
[AsyncInitializer<IWarmUp>(nameof(IWarmUp.WarmUpAsync))]
[Singleton<EspressoMachine>]
// highlight-end
[Singleton<Grinder>]
[Transient<Cup>]
public static partial class CoffeeShop;
```

```text title="Output"
Your cup holds an espresso shot from fresh grounds.
```

`[AsyncInitializer<IWarmUp>]` tells the container that every `IWarmUp` is warmed up by `WarmUpAsync`. The container awaits it once, before anything receives the machine. `IWarmUp` is your own interface, so the machine never references Awaiten.

The `Cup` needs the machine, so it is asynchronous too: resolve it with `ResolveAsync<T>()`. Services without asynchronous setup still resolve synchronously with `Resolve<T>()`.

Use `await using` for the `Root`, so services that dispose asynchronously are torn down correctly.

### Warm up when the shop opens

In an app you usually warm everything up at startup, so no request waits for the boiler. `InitializeAsync` initializes every async singleton ahead of time, in dependency order:

```csharp
await using CoffeeShop.Root shop = new();
await shop.InitializeAsync();   // the machine warms up here, once

Cup cup = await shop.ResolveAsync<Cup>();   // ready immediately
```

Under ASP.NET Core and the generic host, the [MS.DI bridge](./07-hosting/01-msdi-bridge.md#warm-async-services-on-startup) does this for you during host startup.

## Register against an interface

Most services hide behind an interface. Pass the implementation first, then the service type:

```csharp
public interface IBrewer;
public sealed class EspressoBrewer : IBrewer;

[Container]
[AsyncInitializer<IWarmUp>(nameof(IWarmUp.WarmUpAsync))]
[Singleton<EspressoMachine>]
[Singleton<Grinder>]
// highlight-next-line
[Singleton<EspressoBrewer, IBrewer>]
[Transient<Cup>]
public static partial class CoffeeShop;

using CoffeeShop.Root shop = new();
IBrewer brewer = shop.Resolve<IBrewer>();   // the container hands out the EspressoBrewer
```

## Open a scope per order

Some things belong to a single order. Register them as `[Scoped]` and open a scope for each order:

```csharp
[Container]
[AsyncInitializer<IWarmUp>(nameof(IWarmUp.WarmUpAsync))]
[Singleton<EspressoMachine>]
[Singleton<Grinder>]
[Singleton<EspressoBrewer, IBrewer>]
// highlight-next-line
[Scoped<Order>]
[Transient<Cup>]
public static partial class CoffeeShop;

await using CoffeeShop.Root shop = new();

await using (IAwaitenScope order = shop.CreateScope())
{
    Order current = order.Resolve<Order>();
} // the order and everything it owns is disposed here
```

Inside one scope you get the same `Order` every time. A different scope gets a different one. See [Scopes](./03-core-concepts/02-scopes.md) for the full picture.

## Let the compiler check your wiring

Mistakes in the graph do not wait for run time. Let a cup take the espresso machine through a synchronous `Func<T>`, so it could pull a shot before the boiler is hot, and the build stops:

```csharp expect=AWT119
public sealed class Cup(Func<EspressoMachine> machine);   // the machine may still be cold

[Container]
[AsyncInitializer<IWarmUp>(nameof(IWarmUp.WarmUpAsync))]
[Singleton<EspressoMachine>]
[Transient<Cup>]
public static partial class CoffeeShop;
```

```text title="Build error"
error AWT119: 'Cup' resolves 'EspressoMachine' through a synchronous Func<>, but 'EspressoMachine' is async-initialized; resolve it through ResolveAsync, or set AsyncSafety.SyncAfterInit on the [Container]
```

Take `Func<Task<EspressoMachine>>` instead and await it, and the cup only ever gets a warm machine.

A missing registration, a dependency cycle and a singleton that captures a scoped service are caught the same way. Awaiten ships more than 100 diagnostics; the [Diagnostics](./09-diagnostics.md) page lists them all with an example and a fix.

## Next steps

Learn the concepts:

- [Complete example](./02-complete-example.md) for a full container that uses the main features together.
- [Lifetimes](./03-core-concepts/01-lifetimes.md) for singleton, scoped and transient in depth.
- [Resolving services](./05-resolution/01-resolving-services.md) for the full resolve surface.
- [Async initialization](./03-core-concepts/03-async-initialization.md) for conventions with parameters, asynchronous hooks and warming up scopes.

Use it in your app:

- [MS.DI bridge](./07-hosting/01-msdi-bridge.md) to run a container under ASP.NET Core and the generic host.
- [Migrating from MS.DI](./07-hosting/03-migrate-from-msdi.md) to map your `IServiceCollection` registrations to Awaiten.
- [Design principles & the composition root](./08-design-principles.md) for the design behind the mechanics.
- [Benchmarks](./10-benchmarks.mdx) to compare Awaiten with other .NET containers.
