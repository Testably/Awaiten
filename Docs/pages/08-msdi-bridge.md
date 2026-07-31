# MS.DI bridge

Most .NET apps run on Microsoft.Extensions.DependencyInjection. ASP.NET Core, the generic host, and worker services all speak it. The `Awaiten.Extensions.DependencyInjection` package bridges your generated container into that world, so you keep Awaiten's compile-time safety and still fit the host.

Install it alongside the core package:

```sh
dotnet add package Awaiten.Extensions.DependencyInjection
```

## Project into a service collection

`AddGeneratedContainer<TRoot>` registers the container and projects its registrations into the `IServiceCollection`. Awaiten lifetimes align to `ServiceLifetime`, and one Awaiten scope aligns to each MS.DI scope.

```csharp
var services = new ServiceCollection();
services.AddGeneratedContainer<CoffeeShop.Root>();

using var provider = services.BuildServiceProvider();
IBrewer brewer = provider.GetRequiredService<IBrewer>();
```

An async-only service has no synchronous path, so its bare type is not registered. It is available as `Task<T>`, which routes through `ResolveAsync`.

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
probe.IsService(typeof(EspressoMachine));         // false: async-only, ask for Task<EspressoMachine>
```

The answer comes from the container itself, through `IAwaitenContainerMetadata.IsResolvable`, so it covers every shape the container dispatches and not merely what it advertises as a registration: a registration, the [relationship shapes](./resolution/relationships) over it, the synthesized [collections](./resolution/collections) and [keyed dictionaries](./resolution/keyed-dictionaries), the awaited views over those, and a [variance](./advanced/generic-variance)-compatible closing of a registered variant generic interface. The `Task<T>` projection of an async-only registration is reported too, and its bare service type is not, matching what each resolves to.

Wrapping a bare scope rather than a `Root` leaves the provider without metadata, and it then does not offer the probe at all, so the framework keeps its own fallback.

`IsService` answers whether the service *exists*, not whether this scope will build it, which is MS.DI's own semantics. A disposable transient asked on the root is reported, and resolving it there throws the container's guidance naming the fix rather than returning `null`, so the failure lands at the cause.

Two answers still differ from MS.DI, both under-reporting, and both want a `[FromServices]` on the parameter. An [open generic](./registration/open-generics) registration is expanded per closing that something in the container's own graph asks for, so a closing only a framework asks for is never synthesized: with `[Singleton(typeof(Repo<>), typeof(IRepo<>))]` and nothing in the graph consuming `IRepo<Order>`, `IsService(typeof(IRepo<Order>))` is `false` where MS.DI answers `true`. And MS.DI special-cases `IEnumerable<T>`, answering `true` for any `T` because it can always manifest an empty sequence, where a compile-time container has no case emitted for an element type the graph never mentioned. In both the probe is faithful to this container, which has no resolution either; matching MS.DI would mean building a collection for a type unknown at compile time. Each is pinned by a test in `FeatureDetectionTests`.

## Warm async services on startup

`AddAwaitenInitialization<TContainer>` registers an `IHostedService` that calls `InitializeAsync` on startup. It wires the external resolver first, so `[ImportService<T>]` dependencies resolve during warm-up. It is idempotent.

```csharp
services.AddGeneratedContainer<CoffeeShop.Root>();
services.AddAwaitenInitialization<CoffeeShop.Root>();
```

Pair this with `[Container(SyncResolveAfterInit = true)]` so warmed singletons resolve synchronously afterwards.

## Verify external dependencies at startup

An Awaiten graph can depend on host services through `[ImportService<T>]` and `[ImportServices]`. See [External services](./advanced/external-services). Verify they are actually registered before you serve traffic.

```csharp
provider.VerifyAwaitenContainers();
```

If a dependency is missing, verification throws and names it, including keyed ones. This is the runtime version of the compile-time missing-dependency check, across the host boundary.

## Native AOT

The bridge is reflection-free. The generator emits the closed `Task<T>` type and a typed converter into the registration metadata, so the bridge never calls `MakeGenericType` or `MakeGenericMethod`. The container and its bridge are native-AOT clean, which the AOT sample in the repository proves.

## What the bridge does not project

The bridge surfaces each individual registration and the external dependencies. It does not project Awaiten collections into MS.DI-side resolution; those stay internal to the container. If you need the container to be the single disposal owner for multi-service singletons and nested scoped dependencies, use the `AwaitenServiceProvider` replacement path.

## Where to go next

- [Async initialization](./async-initialization) for the initialization the hosted service drives.
- [External services](./advanced/external-services) for `[ImportService<T>]` and `[ImportServices]`.
