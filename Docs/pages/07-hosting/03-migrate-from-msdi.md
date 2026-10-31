---
description: Move an IServiceCollection setup to Awaiten, all at once or one service at a time through the MS.DI bridge.
sidebar_label: Migrating from MS.DI
---

import PackageInstall from '@site/src/components/PackageInstall';

# Migrating from Microsoft.Extensions.DependencyInjection

This page is for apps that register their services on an `IServiceCollection` today, under ASP.NET Core, the generic host or a plain `ServiceProvider`, and want those registrations checked by the compiler instead. It maps each Microsoft.Extensions.DependencyInjection (MS.DI) concept to its Awaiten counterpart, migrates a small coffee shop step by step, and lists what has no direct equivalent.

You do not have to move everything at once. The [MS.DI bridge](./01-msdi-bridge.md) projects an Awaiten container into a service collection, so the host keeps running on MS.DI while you move one group of services after another.

<PackageInstall package={['Awaiten', 'Awaiten.Extensions.DependencyInjection']} />

## What changes

In MS.DI you call methods on a service collection at startup, and the provider wires the graph at run time. In Awaiten you declare the same registrations as attributes on a `static partial` class marked `[Container]`, and a source generator wires the graph at build time. Two consequences shape the whole migration:

- The registrations are fixed when the project compiles. There is no collection to add to, replace in or remove from at run time.
- What `ValidateOnBuild` and `ValidateScopes` find at startup, and what MS.DI reports only on the first resolve, is a build error: a missing dependency is [AWT101](../09-diagnostics.md#awt101), a cycle [AWT102](../09-diagnostics.md#awt102), a singleton capturing a scoped service [AWT105](../09-diagnostics.md#awt105).

## Mapping

| MS.DI | Awaiten |
|---|---|
| `services.AddSingleton<IBrewer, EspressoBrewer>()` | `[Singleton<EspressoBrewer, IBrewer>]`: the implementation comes first, then the service. See [Lifetimes](../03-core-concepts/01-lifetimes.md). |
| `services.AddScoped<Order>()` | `[Scoped<Order>]` |
| `services.AddTransient<Cup>()` | `[Transient<Cup>]` |
| `services.AddSingleton<IBrewer>(sp => ...)` | `[Singleton<IBrewer>(Factory = nameof(MakeBrewer))]` with a static method on the container. Its parameters resolve from the graph, so there is no `sp.GetRequiredService` inside it. See [Factories and instances](../04-registration/01-factories-and-instances.md). |
| `services.AddSingleton(menuBoard)` | `[Singleton<MenuBoard>(Instance = nameof(Menu))]` with a static field or property. As in MS.DI, the container never disposes it. |
| `services.TryAddSingleton<ITimeSystem, RealTimeSystem>()` | `[Singleton<RealTimeSystem, ITimeSystem>(Fallback = Fallback.Silent)]`, typically in a module: it contributes only when nothing else provides the service. See [overridable defaults](../04-registration/07-modules.md#overridable-defaults). |
| An `AddPayments()` extension method on `IServiceCollection` | A static class marked `[Module]`, pulled in with `[Import(typeof(PaymentModule))]`. See [Modules](../04-registration/07-modules.md). |
| Several `AddSingleton<ISyrup, ...>()` calls and an `IEnumerable<ISyrup>` parameter | Several `[Singleton<..., ISyrup>]` registrations and the same `IEnumerable<ISyrup>` parameter; arrays, `IReadOnlyList<T>` and the other [collection shapes](../05-resolution/03-collections.md) work too. A single `ISyrup` resolves to the *first* registration, not the last. |
| `services.AddKeyedSingleton<IMilk, OatMilk>("Oat")` | `[Singleton<OatMilk, IMilk>(Key = "Oat")]` |
| `[FromKeyedServices("Oat")] IMilk milk` | `[FromKey("Oat")] IMilk milk`, or keep the consumer free of attributes with `WhenInjectedInto` or a factory. See [Keyed services](../04-registration/04-keyed-services.md). |
| `services.AddTransient(typeof(IRepository<>), typeof(Repository<>))` | `[Transient(typeof(Repository<>), typeof(IRepository<>))]`. Awaiten generates the closings the graph asks for. See [Open generics](../04-registration/05-open-generics.md). |
| `ActivatorUtilities.CreateInstance<Cup>(sp, "Large")` | An `[Arg]` parameter and a `Func<string, Cup>` dependency. See [Runtime arguments](../05-resolution/06-runtime-arguments.md). |
| `provider.GetRequiredService<Cup>()` | `shop.Resolve<Cup>()` |
| `provider.GetService<Cup>()` | `shop.TryResolve<Cup>(out Cup? cup)` |
| `provider.GetServices<ISyrup>()` | `shop.Resolve<IEnumerable<ISyrup>>()` |
| `provider.GetRequiredKeyedService<IMilk>("Oat")` | `shop.Resolve<IMilk>("Oat")`. See [Resolving services](../05-resolution/01-resolving-services.md). |
| `provider.CreateScope()`, `provider.CreateAsyncScope()` | `shop.CreateScope()`, disposed with `using` or `await using`. `CreateScopeAsync` also initializes the scope's async services up front. See [Scopes](../03-core-concepts/02-scopes.md). |
| `IServiceScopeFactory` injected into a service | `Func<Owned<T>>`: each call builds `T` in its own scope, which the handle disposes. See [Owned](../06-lifetime/01-owned.md). |
| `IDisposable` and `IAsyncDisposable` services | Disposed by the scope or root that built them, in reverse creation order. A service that is only `IAsyncDisposable` needs `await using`, as in MS.DI ([AWT156](../09-diagnostics.md#awt156)). See [Disposal](../03-core-concepts/04-disposal.md). |
| `services.Configure<Settings>(...)` and `IOptions<Settings>` | Configuration stays in MS.DI. The container consumes it with `[ImportService(typeof(IOptions<>))]`. See [External services](./02-external-services.mdx). |
| `services.AddHostedService<Roaster>()` | Stays in MS.DI. The hosted service's constructor can take services the bridge projects from the container. |

## Migrate a coffee shop

The shop below runs on MS.DI. It validates its graph when the provider is built and opens a scope per order.

```csharp
using Microsoft.Extensions.DependencyInjection;

ServiceCollection services = new();
services.AddSingleton<EspressoMachine>();
services.AddSingleton<IBrewer, EspressoBrewer>();
services.AddScoped<Order>();
services.AddTransient<Cup>();
services.AddKeyedSingleton<IMilk, OatMilk>("Oat");
services.AddKeyedSingleton<IMilk, WholeMilk>("Whole");
services.AddSingleton<ISyrup, VanillaSyrup>();
services.AddSingleton<ISyrup, CaramelSyrup>();
services.AddSingleton(MenuBoard.Load());

await using ServiceProvider provider = services.BuildServiceProvider(
    new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

await using (AsyncServiceScope order = provider.CreateAsyncScope())
{
    Order current = order.ServiceProvider.GetRequiredService<Order>();
    IMilk milk = order.ServiceProvider.GetRequiredKeyedService<IMilk>("Oat");
    IEnumerable<ISyrup> syrups = order.ServiceProvider.GetServices<ISyrup>();
}
```

### 1. Declare the container

Translate each call into an attribute on a container class. Swap the type arguments where a service type is named: Awaiten takes the implementation first. The pre-built menu board becomes a static property that `Instance` points at.

```csharp
[Container]
[Singleton<EspressoMachine>]
[Singleton<EspressoBrewer, IBrewer>]
[Scoped<Order>]
[Transient<Cup>]
[Singleton<OatMilk, IMilk>(Key = "Oat")]
[Singleton<WholeMilk, IMilk>(Key = "Whole")]
[Singleton<VanillaSyrup, ISyrup>]
[Singleton<CaramelSyrup, ISyrup>]
[Singleton<MenuBoard>(Instance = nameof(Menu))]
public static partial class CoffeeShop
{
    private static MenuBoard Menu { get; } = MenuBoard.Load();
}
```

### 2. Build the project

There is no `ValidateOnBuild` to set: the build is the validation. If `Cup` needed a `Grinder` that nothing registers, the build fails with [AWT101](../09-diagnostics.md#awt101) instead of the provider throwing at startup. Each entry on the [Diagnostics](../09-diagnostics.md) page ends with the fix.

### 3. Replace the provider with the root

The generated `CoffeeShop.Root` takes the place of the `ServiceProvider`, and its scopes take the place of the MS.DI scopes.

```csharp
await using CoffeeShop.Root shop = new();

await using (IAwaitenScope order = shop.CreateScope())
{
    Order current = order.Resolve<Order>();
    IMilk milk = order.Resolve<IMilk>("Oat");
    IEnumerable<ISyrup> syrups = order.Resolve<IEnumerable<ISyrup>>();
}
```

### 4. Keep the host

Under ASP.NET Core or the generic host, the host still needs an `IServiceCollection`. Project the container into it, and warm any async services on startup:

```csharp
using Awaiten.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;

WebApplicationBuilder builder = WebApplication.CreateBuilder();
builder.Services.AddAwaitenContainer<CoffeeShop.Root>()
                .WithInitialization();
```

Controllers, minimal API handlers and hosted services now receive the container's services from MS.DI. See the [MS.DI bridge](./01-msdi-bridge.md) for the details, including how scopes and disposal line up.

## Migrate incrementally

A large app rarely moves in one step. With the bridge, both containers serve the same host while you migrate:

1. Pick a group of services that belong together, such as everything behind the counter, and move their registrations into a `[Container]`.
2. Project the container with `AddAwaitenContainer<CoffeeShop.Root>()`. The services that stay in MS.DI keep their registrations and can take the projected services as constructor parameters.
3. Where a moved service still depends on a service MS.DI registers, declare it with `[ImportService<T>]` on the container. Every other missing dependency remains a build error.
4. Call `VerifyAwaitenContainers()` on the provider at startup, which throws and names any imported service the host does not register.
5. Remove the moved registrations from the service collection, and repeat with the next group.

```csharp
using Awaiten.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

[Container]
[ImportService<IPaymentGateway>]   // still registered in MS.DI
[Singleton<EspressoMachine>]
[Scoped<Order>]
[Transient<Barista>]
public static partial class CoffeeShop;

ServiceCollection services = new();
services.AddSingleton<IPaymentGateway, StripeGateway>();   // not migrated yet
services.AddAwaitenContainer<CoffeeShop.Root>();

await using ServiceProvider provider = services.BuildServiceProvider();
provider.VerifyAwaitenContainers();
```

While a group is half moved, `[ImportServices]` lets every unregistered dependency fall through to the host instead of naming each one. It trades away the missing-dependency check, so replace it with `[ImportService<T>]` declarations once the group is done. See [External services](./02-external-services.mdx).

:::tip
`LifetimeSafety.Loose` on the container lets a disposable transient resolve from the root, as MS.DI allows. It can help while code still relies on that, but the strict default with [`Owned<T>`](../06-lifetime/01-owned.md) is the leak-free target. See [Lifetime safety](../06-lifetime/02-lifetime-safety.md).
:::

## What has no direct equivalent

- **Changing registrations at run time.** `Replace`, `RemoveAll`, or registering only `if (configuration["UseFilter"] == "true")` have no counterpart, because the graph is built at compile time. Make the choice in a [factory method](../04-registration/01-factories-and-instances.md#factory) that takes the setting as a parameter, or keep that registration in MS.DI.
- **Last registration wins.** In MS.DI a later registration overrides an earlier one for single resolution. In Awaiten the first one wins, and an override is declared explicitly: a module offers an overridable default with `Fallback`, and the container's own registration replaces it.
- **Lambda factories.** A factory is a named static method on the container or a module, not a closure. Values it needs come from its parameters or from an `Instance` member.
- **`KeyedService.AnyKey`.** A key selects exactly one registration; there is no wildcard key. To get every keyed implementation at once, take an [`IReadOnlyDictionary<string, IMilk>`](../05-resolution/04-keyed-dictionaries.md).
- **Injecting `IServiceProvider` or `IServiceScopeFactory` into a service.** Resolving from the container inside a service is the service locator pattern, which Awaiten reports when its own resolver interfaces are injected ([AWT135](../09-diagnostics.md#awt135)). Take the dependency itself, a `Func<T>`, a `Func<Owned<T>>`, or a [dispatch dictionary](../05-resolution/04-keyed-dictionaries.md#dispatch-by-runtime-type) for a type known only at run time.
- **Disposable transients on the root.** MS.DI keeps every disposable transient resolved from the root provider until the provider is disposed. Awaiten's strict default withholds them from the root and points you to `Owned<T>` or a scope; `LifetimeSafety.Loose` restores the MS.DI behavior.
- **Collections across the bridge.** The bridge projects each registration, not Awaiten's synthesized collections, so consume `IEnumerable<T>` of container services inside the container.
- **Open generic closings only the host asks for.** The container generates the closings its own graph uses. A closing that only a framework requests, for example as a minimal API parameter, is not available through the bridge. See [Feature detection](./01-msdi-bridge.md#feature-detection).
- **Options, logging and hosted services.** They stay registered in MS.DI. The container imports what it needs, such as `[ImportService(typeof(ILogger<>))]`.

MS.DI has no counterpart to [async initialization](../03-core-concepts/03-async-initialization.md) either: a service that needs asynchronous setup implements `IAsyncInitializable`, and the bridge exposes it to MS.DI as `Task<T>`.

## Where to go next

- [MS.DI bridge](./01-msdi-bridge.md) for the projection, scopes and disposal under a host.
- [External services](./02-external-services.mdx) for services that stay in MS.DI.
- [Diagnostics](../09-diagnostics.md) for every build error and its fix.
