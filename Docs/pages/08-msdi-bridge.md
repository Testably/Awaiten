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

The answer comes from the container itself, through `IAwaitenContainerMetadata.IsResolvable`, so it covers every shape the container dispatches and not merely what it advertises as a registration: a registration, the [relationship shapes](./resolution/relationships) over it, the synthesized [collections](./resolution/collections) and [keyed dictionaries](./resolution/keyed-dictionaries), the awaited views over those, and a [variance](./advanced/generic-variance)-compatible closing of a registered variant generic interface. The `Task<T>` projection of an async-only registration is reported that way. Its bare service type is reported too, but from `WithheldReason` below rather than from `IsResolvable`, which answers only what the container dispatches; asking for it then throws the guidance naming the projection.

Wrapping a bare scope rather than a `Root` leaves the provider without metadata, and it then does not offer the probe at all, so the framework keeps its own fallback.

`IsService` answers whether the service *exists*, not whether this scope will build it, which is MS.DI's own semantics. A disposable transient asked on the root is reported, and so is an async-initialized service asked synchronously; resolving either throws the container's guidance naming the fix rather than returning `null`, so the failure lands at the cause instead of letting a host bind the parameter from somewhere else.

That distinction is available to your own code through `IAwaitenContainerMetadata.WithheldReason`. It returns the message `Resolve` would throw, or `null` when the container has no such reason, so a failed `TryResolve` can be told apart from a withheld one: one is genuinely not the container's service, the other is a mistake with a named fix. Any adapter that must answer an unknown type with `null`, because that is how it says "not mine" to a framework, wants this before it decides between returning `null` and throwing. It covers the keyed surface under a non-null key, exactly as the keyed `Resolve` does. Ask it on the `Root`, which is where the answer belongs: a child scope builds a root-withheld transient normally, so only the root can say it is withholding one.

`IEnumerable<T>` resolves for every `T`, empty when the container has no *unkeyed* registration of the element type, matching the guarantee MS.DI consumers rely on: a collection is a framework's usual extension point, and "every registered handler, of which there may be none" has to answer rather than hand back `null`. A service type registered only under keys counts as empty here, exactly as in MS.DI, where keyed registrations do not join an unkeyed `IEnumerable<T>`. The guarantee covers `IEnumerable<T>` alone, again as in MS.DI, which answers `null` for `T[]`, `IList<T>` and `IReadOnlyList<T>` of an unregistered element type. Where the container does have unkeyed members and simply cannot produce the shape for them, such as a collection with an async-initialized member, resolution throws the container's guidance rather than reporting an empty sequence that would silently drop them.

Two answers still under-report, and both want a `[FromServices]` on the parameter. An [open generic](./registration/open-generics) registration is expanded per closing that something in the container's own graph asks for, so a closing only a framework asks for is never synthesized: with `[Singleton(typeof(Repo<>), typeof(IRepo<>))]` and nothing in the graph consuming `IRepo<Order>`, `IsService(typeof(IRepo<Order>))` is `false` where MS.DI answers `true`. And an `IEnumerable<T>` over a *value* element type the graph never mentioned stays unreported, which is a native-AOT constraint rather than a semantic one: the empty sequence needs the `T[]` type, which AOT generates on demand for a reference element type but not for a value one. In both the probe is faithful to what the bridge will do. Each is pinned by a test in `FeatureDetectionTests`.

Writing an adapter for a framework whose dependency-injection surface is not MS.DI means honouring the same convention yourself, because it is the framework's expectation rather than MS.DI's implementation detail. A resolver that answers `null` for an unresolvable `IEnumerable<T>` breaks any framework that uses a collection as an extension point, and the failure names the collection rather than the cause. Answer an empty array of the element type instead, and only for a reference element type, for the AOT reason above.

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
