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

## Use it as the host's provider factory

For the generic host or `WebApplicationBuilder`, plug in `AwaitenServiceProviderFactory<TRoot>`. Host registrations and Awaiten services coexist, so a host component can inject an Awaiten service and the other way round.

```csharp
builder.Host.UseServiceProviderFactory(
    new AwaitenServiceProviderFactory<CoffeeShop.Root>());
```

## Or let Awaiten own everything

`AwaitenServiceProvider` adapts an Awaiten scope directly to `IServiceProvider` and `IServiceScopeFactory`. Awaiten is then the single owner of construction and disposal. `GetService` maps to `TryResolve`, so a missing service returns `null`.

```csharp
using var shop = new CoffeeShop.Root();
using var provider = new AwaitenServiceProvider(shop);

using IServiceScope scope = provider.CreateScope();
```

Pass `ownsContainer: false` if you want to keep ownership of the container yourself.

## Warm async services on startup

`AddAwaitenInitialization<TContainer>` registers an `IHostedService` that calls `InitializeAsync` on startup. It wires the external resolver first, so `[FromServices]` dependencies resolve during warm-up. It is idempotent.

```csharp
services.AddGeneratedContainer<CoffeeShop.Root>();
services.AddAwaitenInitialization<CoffeeShop.Root>();
```

Pair this with `[Container(SyncResolveAfterInit = true)]` so warmed singletons resolve synchronously afterwards.

## Verify external dependencies at startup

An Awaiten graph can depend on host services through `[FromServices]` and `[ImportServices]`. See [External services](./advanced/external-services). Verify they are actually registered before you serve traffic.

```csharp
provider.VerifyAwaitenContainers();
```

If a dependency is missing, verification throws and names it, including keyed ones. This is the runtime version of the compile-time missing-dependency check, across the host boundary.

## Native AOT

The bridge is reflection-free. The generator emits the closed `Task<T>` type and a typed converter into the registration metadata, so the bridge never calls `MakeGenericType` or `MakeGenericMethod`. The container and its bridge are native-AOT clean, which the AOT sample in the repository proves.

## What the bridge does not project

The bridge surfaces each individual registration and the external dependencies. It does not project Awaiten collections or keyed services into MS.DI-side resolution. Those stay internal to the container. If you need the container to be the single disposal owner for multi-service singletons and nested scoped dependencies, use the `AwaitenServiceProvider` replacement path.

## Where to go next

- [Async initialization](./async-initialization) for the initialization the hosted service drives.
- [External services](./advanced/external-services) for `[FromServices]` and `[ImportServices]`.
