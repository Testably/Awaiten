# MS.DI bridge

Most .NET apps run on Microsoft.Extensions.DependencyInjection. ASP.NET Core, the generic host, and worker services all speak it. The `Awaiten.Extensions.DependencyInjection` package bridges your generated container into that world, so you keep Awaiten's compile-time safety and still fit the host.

Install it alongside the core package:

```sh
dotnet add package Awaiten.Extensions.DependencyInjection
```

## Project into a service collection

`AddAwaitenContainer<TRoot>` registers the container and projects its registrations into the `IServiceCollection`. Awaiten lifetimes align to `ServiceLifetime`, and one Awaiten scope aligns to each MS.DI scope.

```csharp
using Awaiten.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddAwaitenContainer<CoffeeShop.Root>();

using var provider = services.BuildServiceProvider();
IBrewer brewer = provider.GetRequiredService<IBrewer>();
```

It returns an `AwaitenContainerBuilder<TRoot>` for the container's hosting options, such as [startup initialization](#warm-async-services-on-startup). Its `Services` property is the collection, for chaining further registrations.

To configure the root before the host sees it, for example to construct it with your own external resolver, build it yourself and pass it in. The root is projected as-is and stays yours, as with any instance you register in MS.DI: the provider never disposes it, so dispose it after the provider.

```csharp
using var shop = new CoffeeShop.Root(myResolver);
services.AddAwaitenContainer(shop);
```

An async-only service has no synchronous path, so this projection does not register its bare type. It is available as `Task<T>`, which routes through `ResolveAsync`. (Under the [replacement path](#or-let-awaiten-own-everything) the bare type does resolve, into the container's guidance naming this projection, because there the container answers every request rather than contributing descriptors.)

```csharp
Task<EspressoMachine> machine = provider.GetRequiredService<Task<EspressoMachine>>();
```

A keyed registration is projected under its key, so `[FromKeyedServices]` and `GetKeyedService` reach it. An async-only keyed service is projected as a keyed `Task<T>`, the same way its unkeyed counterpart is.

```csharp
public sealed class Router([FromKeyedServices("fast")] IChannel channel);
```

## Use it as the host's provider factory

For the generic host or `WebApplicationBuilder`, plug in `AwaitenServiceProviderFactory<TRoot>`. Host registrations and Awaiten services coexist, so a host component can inject an Awaiten service and the other way round.

```csharp
builder.Host.UseServiceProviderFactory(
    new AwaitenServiceProviderFactory<CoffeeShop.Root>());
```

The factory takes an optional callback that receives the same `AwaitenContainerBuilder<TRoot>`, so its options read the same on both paths:

```csharp
builder.Host.UseServiceProviderFactory(
    new AwaitenServiceProviderFactory<CoffeeShop.Root>(c => c.WithInitialization()));
```

## Disposal

On both paths above, Awaiten disposes everything it built exactly once, with the scope that owns it. The Awaiten scope aligned to an MS.DI scope is disposed with that scope, and the container root with the root provider. (After a plain `AddAwaitenContainer<TRoot>()`, the root provider takes over the root on the first bridged resolution or when `WithInitialization()` warms it, so dispose a root you only use directly yourself. A root you passed to `AddAwaitenContainer(root)` is always yours to dispose.) That includes disposables reached only as nested dependencies, which MS.DI never sees, and instances awaited through `Task<T>`. Disposing the provider or a scope with `DisposeAsync` drains them asynchronously, so `IAsyncDisposable`-only services are supported there. A pre-built `Instance` member stays yours and is never disposed.

MS.DI additionally calls `Dispose` on every instance a projected registration hands out, once per service type it was resolved as, before the Awaiten scope or root that built it. That is how MS.DI treats any factory registration, for example `AddSingleton<IWriter>(sp => sp.GetRequiredService<Ledger>())`, and it relies on `Dispose` being idempotent, as the `IDisposable` contract requires. A singleton `Ledger` resolved as both `IReader` and `IWriter` therefore sees three `Dispose` calls: one per service type from MS.DI, then one from Awaiten.

That is also why a service built with `SuppressDisposal = true` cannot be projected when it is or may be disposable: MS.DI would dispose it no matter what the container promised. Both paths above throw at startup instead, naming the service, and [AWT214](../diagnostics#awt214) reports the projection at build time. Remove `SuppressDisposal`, register a pre-built `Instance` you own, or let Awaiten own everything as described next. A service that needs asynchronous initialization is unaffected, because MS.DI only receives its `Task<T>`.

## Or let Awaiten own everything

`AwaitenServiceProvider` adapts an Awaiten scope directly to `IServiceProvider`, `IKeyedServiceProvider`, and `IServiceScopeFactory`. Awaiten is then the single owner of construction and disposal. `GetService` maps to `TryResolve`, so a missing service returns `null`, and `GetKeyedService`/`GetRequiredKeyedService` reach a keyed registration by its key.

```csharp
using var shop = new CoffeeShop.Root();
using var provider = new AwaitenServiceProvider(shop);

using IServiceScope scope = provider.CreateScope();
```

Pass `ownsContainer: false` if you want to keep ownership of the container yourself.

### Feature detection

The provider also answers `IServiceProviderIsService` and `IServiceProviderIsKeyedService`, from the container's registration metadata and without constructing anything. ASP.NET Core depends on this: minimal APIs ask it whether a handler parameter comes from dependency injection or from the request, and MVC's controller activation asks the same.

```csharp
var probe = provider.GetRequiredService<IServiceProviderIsService>();
probe.IsService(typeof(IBrewer));                 // true
probe.IsService(typeof(EspressoMachine));         // true: it exists, but resolving it names Task<EspressoMachine>
```

The answer comes from the container itself, through `IAwaitenRoot.GetResolvability`, so it covers every shape the container dispatches and not merely what it advertises as a registration: a registration, the [relationship shapes](../resolution/relationships) over it, the synthesized [collections](../resolution/collections) and [keyed dictionaries](../resolution/keyed-dictionaries), the awaited views over those, and a [variance](../resolution/generic-variance)-compatible closing of a registered variant generic interface. The bridge adds the `Task<T>` projection of an async-only registration. Its bare service type is reported too, because the container has it (as `AsyncOnly`); asking for it then throws the guidance naming the projection.

Wrapping a bare scope rather than a `Root` leaves the provider without metadata, and it then does not offer the probe at all, so the framework keeps its own fallback.

`IsService` answers whether the service *exists*, not whether this scope will build it, which is MS.DI's own semantics. A disposable transient asked on the root is reported, and so is an async-initialized service asked synchronously; resolving either throws the container's guidance naming the fix rather than returning `null`, so the failure lands at the cause instead of letting a host bind the parameter from somewhere else.

That distinction is available to your own code through `IAwaitenRoot.GetResolvability`. Its `Status` is `NotRegistered`, `Resolvable`, `AsyncOnly` (the service needs asynchronous initialization) or `WithheldOnRoot` (a disposable transient the root declines to build), and for the last two its `Reason` carries the message `Resolve` would throw. So a failed `TryResolve` can be told apart from a withheld one: one is genuinely not the container's service, the other is a mistake with a named fix. Any adapter that must answer an unknown type with `null`, because that is how it says "not mine" to a framework, wants this before it decides between returning `null` and throwing. It covers the keyed surface under a non-null key, exactly as the keyed `Resolve` does. The shorter `GetResolvability(Type)` and `GetResolvability<T>()` ask without a key, matching `Resolve(Type)` and `Resolve<T>()`. It lives on the root because that is where the answer belongs: a child scope builds a root-withheld transient normally. New statuses may be added later, so give a `switch` over `Status` a default case.

```csharp
AwaitenResolvability answer = shop.GetResolvability<EspressoMachine>();
if (answer.Reason is { } reason)
{
    throw new InvalidOperationException(reason);   // AsyncOnly or WithheldOnRoot: the fix is named
}
```

`IEnumerable<T>` resolves for every `T`, empty when the container has no *unkeyed* registration of the element type, matching the guarantee MS.DI consumers rely on: a collection is a framework's usual extension point, and "every registered handler, of which there may be none" has to answer rather than hand back `null`. A service type registered only under keys counts as empty here, exactly as in MS.DI, where keyed registrations do not join an unkeyed `IEnumerable<T>`. The guarantee covers `IEnumerable<T>` alone, again as in MS.DI, which answers `null` for `T[]`, `IList<T>` and `IReadOnlyList<T>` of an unregistered element type. Where the container does have unkeyed members and simply cannot produce the shape for them, such as a collection with an async-initialized member, resolution throws the container's guidance rather than reporting an empty sequence that would silently drop them.

Two answers still under-report, and both want a `[FromServices]` on the parameter. An [open generic](../registration/open-generics) registration is expanded per closing that something in the container's own graph asks for, so a closing only a framework asks for is never synthesized: with `[Singleton(typeof(Repo<>), typeof(IRepo<>))]` and nothing in the graph consuming `IRepo<Order>`, `IsService(typeof(IRepo<Order>))` is `false` where MS.DI answers `true`. And an `IEnumerable<T>` over a *value* element type the graph never mentioned stays unreported, which is a native-AOT constraint rather than a semantic one: the empty sequence needs the `T[]` type, which AOT generates on demand for a reference element type but not for a value one. In both the probe is faithful to what the bridge will do. Each is pinned by a test in `FeatureDetectionTests`.

Writing an adapter for a framework whose dependency-injection surface is not MS.DI means honouring the same convention yourself, because it is the framework's expectation rather than MS.DI's implementation detail. A resolver that answers `null` for an unresolvable `IEnumerable<T>` breaks any framework that uses a collection as an extension point, and the failure names the collection rather than the cause. Answer an empty array of the element type instead, and only for a reference element type, for the AOT reason above.

## Warm async services on startup

`WithInitialization()` registers an `IHostedService` that calls `InitializeAsync` on startup. It wires the external resolver first unless you set one, so `[ImportService<T>]` dependencies resolve during warm-up. It is idempotent.

```csharp
services.AddAwaitenContainer<CoffeeShop.Root>()
        .WithInitialization();
```

Pair this with `[Container(AsyncSafety = AsyncSafety.SyncAfterInit)]` so warmed singletons resolve synchronously afterwards.

## Verify external dependencies at startup

An Awaiten graph can depend on host services through `[ImportService<T>]` and `[ImportServices]`. See [External services](./external-services). Verify they are actually registered before you serve traffic.

```csharp
provider.VerifyAwaitenContainers();
```

If a dependency is missing, verification throws and names it, including keyed ones. This is the runtime version of the compile-time missing-dependency check, across the host boundary.

## Native AOT

The bridge is reflection-free. The generator emits the closed `Task<T>` type and a typed converter into the registration metadata, so the bridge never calls `MakeGenericType` or `MakeGenericMethod`. The container and its bridge are native-AOT clean, which the AOT sample in the repository proves.

## What the bridge does not project

The bridge surfaces each individual registration and the external dependencies. It does not project Awaiten collections into MS.DI-side resolution; those stay internal to the container.

## Where to go next

- [Async initialization](../core-concepts/async-initialization) for the initialization the hosted service drives.
- [External services](./external-services) for `[ImportService<T>]` and `[ImportServices]`.
