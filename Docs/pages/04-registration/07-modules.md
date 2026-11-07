---
description: Bundle registrations into reusable modules, import them into a container and ship internal implementations and self-compiled scans from a library.
---

# Modules

A module is a reusable bundle of registrations. It lets you group everything for payments, or for the seasonal menu, and drop it into a container with one line. Modules can live in other assemblies, so a shared library can ship its own wiring.

## Declare a module

A module is a `static` class marked `[Module]`. It carries the same registration attributes a container does.

```csharp
[Module]
[Singleton<CardTerminal, IPaymentTerminal>]
[Singleton<ReceiptPrinter, IReceiptPrinter>]
public static class PaymentModule;
```

## Import it

Pull a module into a container with `[Import]`.

```csharp
[Container]
[Import(typeof(PaymentModule))]
[Singleton<EspressoMachine>]
public static partial class CoffeeShop;
```

Everything the module registers is now part of the container.

## Overridable defaults

A module can offer a default that the container is free to replace. Mark it `Fallback = Fallback.Warn`. If the container registers the same service, its own registration wins and the default is dropped entirely, even from collections. Use `Fallback = Fallback.Silent` for a registration that contributes only when nothing else provides the service; unlike `Warn`, it stays quiet when another overridable default competes with it (`Warn` reports [AWT148](../09-diagnostics.md#awt148) in that case). The choice belongs to the module author on purpose: it sets the collision severity per registration, which a consumer's `.editorconfig` cannot do, because a diagnostic severity there applies to the whole project.

```csharp
[Module]
[Singleton<RealTimeSystem, ITimeSystem>(Fallback = Fallback.Warn)]
public static class InfrastructureModule;

[Container]
[Import(typeof(InfrastructureModule))]
[Singleton<MockTimeSystem, ITimeSystem>]   // wins over the module default
public static partial class CoffeeShop;
```

An [open generic](./05-open-generics.md#overridable-defaults) default works the same way, per closing. The container can replace a single closing with a closed registration, or every closing with its own open registration:

```csharp
[Module]
[Singleton(typeof(MemoryCache<>), typeof(ICache<>), Fallback = Fallback.Warn)]
public static class CachingModule;

[Container]
[Import(typeof(CachingModule))]
[Singleton<OrderCache, ICache<Order>>]   // ICache<Order> is OrderCache, every other closing MemoryCache<T>
public static partial class CoffeeShop;
```

## Factories and instances in modules

A module can use `Factory` and `Instance` too. Awaiten calls the members qualified by the module type, so this works across assemblies. A member doesn't have to be public: for an `internal` member of a public, `partial` module, the module generates a public wrapper, and a container in another assembly calls the wrapper. This works when the member's parameter and return types are public and the member isn't generic. The `OnActivated` or `OnRelease` hook of a `Factory` registration can be `internal` too. The module generates a public wrapper for it, so its parameter types must be public ([AWT203](../09-diagnostics.md#awt203)) and carry no `[FromKey]` or `[Inject]` ([AWT204](../09-diagnostics.md#awt204)). When the library grants `InternalsVisibleTo` to any assembly, such a hook gets no wrapper and no error instead: a container in an assembly the library grants `InternalsVisibleTo` calls the hook directly, and any other container reports [AWT231](../09-diagnostics.md#awt231). A container registration of the same service, its own `Factory` or `Instance` included, wins over the module's, which stays a member of the collection (see [several producers of one service](./01-factories-and-instances.md#several-producers-of-one-service)).

```csharp
[Module]
[Singleton<MemoryCache, ICache>(Instance = nameof(Cache))]
public static class ProductionModule
{
    public static MemoryCache Cache { get; } = new();
}
```

## Internal implementations

A library often keeps its implementations `internal` and exposes only interfaces. Its module registers them like any other type, and a container in another assembly imports the module without the library granting `InternalsVisibleTo`.

```csharp
public interface IKettle;
internal sealed class Kettle(ITimeSystem time) : IKettle;   // stays internal

[Module]
[Singleton<Kettle, IKettle>]
public static partial class KettleModule;   // partial, so the generator can add the factory
```

```csharp
[Container]
[Import(typeof(KettleModule))]
[Singleton<RealTimeSystem, ITimeSystem>]
public static partial class CoffeeShop;
```

When another assembly cannot construct a registered implementation, because the type or every constructor of it is not public, the module compiles the registration **in its own build**: it emits a public factory that constructs the implementation and returns the registered service, or the implementation itself when it is public. A container in another assembly calls that factory instead of the constructor it cannot reach, and keeps everything else the registration declares: the lifetime, `Eager`, `Key`, `WhenInjectedInto`, `Fallback`, `SuppressDisposal`, its place in collections, and the decorators the container adds. It reads from the implementation's metadata which of the module's [async initializer conventions](../03-core-concepts/03-async-initialization.md#which-conventions-apply) it matches and whether it is disposable, so it initializes, disposes and shares the implementation exactly as one it constructs itself. An `OnActivated` or `OnRelease` hook may take the internal type: the module emits a public wrapper for it. A module or service another assembly cannot name never reaches such a container, so it self-compiles nothing.

The module keeps every factory and wrapper it generates in a nested class named `Internals`, hidden from IntelliSense, so they stay apart from the module's own members. The name is reserved: a module or container that declares a member named `Internals` itself is an error ([AWT235](../09-diagnostics.md#awt235)).

Because diagnostics are reported in the library's build, it is the library author who sees any problem. The module must be static ([AWT152](../09-diagnostics.md#awt152)), `partial` ([AWT194](../09-diagnostics.md#awt194)) and non-generic ([AWT201](../09-diagnostics.md#awt201)). The constructor's parameter types must be public, or services the module registers under an internal type (see [services only the module uses](#services-only-the-module-uses)), and not private to the module or a class that contains it ([AWT195](../09-diagnostics.md#awt195)). An `[Arg]` or `[FromKey]` on a constructor parameter is copied onto the factory's parameter, so a container in another assembly passes the runtime argument or resolves the keyed service just as the constructor asks. An `[Inject]` property becomes a factory parameter with the property's name, and the factory assigns it. A property marked `Optional` becomes an optional parameter: a container that doesn't register the service passes `null`, and the factory leaves the property as it is. A property marked `Deferred` gets a generated setter, which a container calls after it built the instance, so it can still break a cycle. Such a property needs a plain `set` accessor, an `Optional` one a reference type, and a property can't be both ([AWT200](../09-diagnostics.md#awt200)). A hook's parameters must be public too ([AWT203](../09-diagnostics.md#awt203)) and carry no `[FromKey]` ([AWT204](../09-diagnostics.md#awt204)). A convention of the module that initializes the implementation must have a public type and method ([AWT234](../09-diagnostics.md#awt234)).

The factory returns one type, and every service of the implementation must convert from it, so that one instance serves them all. That type is the implementation when it's public. Otherwise it's a registered service that covers the others, or else a public base class or interface of the implementation that covers them. An internal implementation registered as `IGreeter` and as `IFormalGreeter : IGreeter` gets a factory that returns `IFormalGreeter`. Without a covering type, the module shares the one instance through a generated handle instead (see [services only the module uses](#services-only-the-module-uses)). When one of the module's scans also matches the implementation, it's rejected ([AWT197](../09-diagnostics.md#awt197)), because a second factory would split the instance a container shares.

The implementation can also be a private type nested in the module, or in a class that contains the module. The module can construct such a type, but other code in the assembly can't, so every container calls the module's factory, even a container in the same assembly. Its hooks can be private too. The module always generates this factory, so it must be `partial`, even when it is `internal`. The factory of an `internal` module may take internal parameter types, because only the module's assembly and the assemblies it grants `InternalsVisibleTo` can call it. The same holds for the parameters of its hooks and for its conventions. Every class that contains the module must be `partial` as well ([AWT194](../09-diagnostics.md#awt194)).

```csharp
[Module]
[Singleton<Kettle, IKettle>(OnActivated = nameof(Preheat))]
public static partial class TeaModule
{
    private sealed class Kettle(ITimeSystem time) : IKettle;

    private static void Preheat(Kettle kettle) { }
}
```

Some of these rules relax when the library grants `InternalsVisibleTo` to any assembly. When the factory can't support an implementation ([AWT195](../09-diagnostics.md#awt195), [AWT197](../09-diagnostics.md#awt197), [AWT200](../09-diagnostics.md#awt200) or [AWT234](../09-diagnostics.md#awt234)), the module generates no factory for it and reports no error. It records the reason instead. The same goes for an implementation that one of the module's scans also matches: the module generates neither the registration's factory nor the scan's, and records both. A hook whose wrapper can't mirror its parameters ([AWT203](../09-diagnostics.md#awt203), [AWT204](../09-diagnostics.md#awt204)) gets no wrapper the same way, unless the hook is generic. A container in an assembly that the library grants `InternalsVisibleTo` constructs the implementation itself, like any type it can reach. A container in any other assembly reports [AWT231](../09-diagnostics.md#awt231) at its `[Import]`, with the reason. The same goes for a module without a `[Scan]` that is generic or not `partial`, so it can't carry any factory ([AWT194](../09-diagnostics.md#awt194), [AWT201](../09-diagnostics.md#awt201)): it builds, and a container in any other assembly reports [AWT231](../09-diagnostics.md#awt231). This doesn't apply to a type private to the module or to a class that contains it, because no other assembly can construct that type, so its errors stay.

Any other implementation a container in the module's own assembly constructs directly, through the same constructor the factory uses, so moving a module between projects never changes what it registers. Importing a module whose assembly was built without the Awaiten generator, or with a version predating this, is an error ([AWT154](../09-diagnostics.md#awt154)) instead of a missing constructor. A decorator or composite another assembly cannot construct gets a factory too. It mirrors the constructor, the inner service of a decorator and the collection of a composite included, and returns the service. So a container in another assembly keeps the decorator's place in the chain and passes the composite the other registrations of the service. A decorator or composite private to the module, or to a class that contains it, is built through this factory by every container. Open generic registrations are not self-compiled: a container in another assembly reports an internal one as [AWT230](../09-diagnostics.md#awt230), as it does for a decorator or composite whose factory the module can't generate, for example because the module isn't `partial` or a constructor parameter type isn't public.

## Services only the module uses

A module can register a service under an internal type that only its own implementations depend on. A container in another assembly can't name that type, but it still builds, shares and disposes the service.

```csharp
public interface IGrinder;
internal sealed class BeanStore;                           // internal service
internal sealed class Grinder(BeanStore beans) : IGrinder;

[Module]
[Singleton<BeanStore>]
[Singleton<Grinder, IGrinder>]
public static partial class CoffeeModule;
```

A container in another assembly imports `CoffeeModule` and resolves `IGrinder`. It never sees `BeanStore`.

For each such service, the module generates a public handle class in its `Internals` class. The handle holds the instance, and only the module's assembly can create or unwrap it. The factory of `Grinder` takes the handle and passes the `BeanStore` it holds to the constructor. The module also generates an instance handle for each implementation it builds this way. The container registers the instance handle once, with the lifetime and other options of the implementation's registrations, and serves every service of the implementation from it. So it checks lifetimes, async initialization and missing dependencies through it as for any service. The registrations of one implementation must agree on their lifetime, production and directives, as in the library's own assembly. The library's build reports a conflict ([AWT107](../09-diagnostics.md#awt107), [AWT111](../09-diagnostics.md#awt111), [AWT166](../09-diagnostics.md#awt166)).

A handle is a dependency only. The container never resolves it directly, never lists it in `Registrations`, and never adds it to an `IServiceCollection`. Diagnostics name the service instead of the handle, for example `'Lib.BeanStore (internal to module Lib.CoffeeModule)'`.

The container treats the internal service like one it constructs itself:

- A singleton is shared by all its dependents, a scoped service is built once per scope, and a transient once per dependency.
- The container disposes the instance, synchronously or asynchronously, as the implementation implements it. `SuppressDisposal` works as usual.
- `OnActivated` and `OnRelease` hooks run through generated wrappers, and so do the module's [async initializer conventions](../03-core-concepts/03-async-initialization.md#which-conventions-apply). The hooks and conventions may stay internal.
- An internal service may depend on other internal services of the module, and an `Eager` or async-initialized internal singleton is built even when nothing public depends on it. A module that isn't `partial` can't generate its handle and is reported ([AWT194](../09-diagnostics.md#awt194)). When its assembly grants `InternalsVisibleTo`, the library build leaves the singleton to those assemblies instead, and any other container reports that it can't build it ([AWT231](../09-diagnostics.md#awt231)).

A dependent may take the internal service directly or as `Func<T>`, `Lazy<T>`, `Owned<T>`, `Func<Owned<T>>` or a collection of `T`. Any other shape, such as `Task<T>`, is an error ([AWT237](../09-diagnostics.md#awt237)).

One implementation registered under an internal service and under other services is one instance, since the container serves every service from its instance handle. The same happens for an internal implementation registered under several public services that no public type covers.

All modules of an assembly share one handle per internal service. The module first by fully qualified name among those that register the service declares it. A module may take an internal service that only another module registers, and a container in another assembly then needs to import both modules ([AWT238](../09-diagnostics.md#awt238)). When two modules register the same implementation, a container in another assembly builds one instance, as a container in the library's own assembly does. Both modules build it into one instance handle, which the module first by fully qualified name among those that register the implementation declares. This holds under whichever services they register it, so a module that registers it only under a public service uses the instance handle too. The container takes the hooks, conventions and other options from the module it imports. When it imports several of them, the first import wins and a conflicting directive is reported ([AWT166](../09-diagnostics.md#awt166)), as in the library's own assembly. In an assembly with several modules, each module builds every internal service it can, since another module may need it. A module that isn't `partial`, or can't build the service on its own, can't share its handle. The library build then reports [AWT236](../09-diagnostics.md#awt236).

A `[Decorate]` of the module may decorate an internal service, with an internal or a public decorator. The module builds each decorator into a handle of the service, so the container keeps the order, keys and lifetime of the chain and disposes each link on its own. When the implementation is registered under other services too, those services serve the undecorated instance, as in the library's own assembly. A `[Composite]` over an internal service gets no handle, so a container in another assembly reports it as [AWT230](../09-diagnostics.md#awt230).

An internal service registered with `Factory` or `Instance` gets a handle too. The module generates a public wrapper that calls the member and puts its result into the handle. The factory's parameters may be internal services as well, and an async factory gets an async wrapper. The handle disposes what the factory returned, as the container would, and never disposes an `Instance`. An internal service registered as an open generic is still reported as [AWT195](../09-diagnostics.md#awt195) when a public registration needs it. The same goes for an internal service whose own implementation or factory the module can't wrap. When one of the module's scans matches an implementation the module builds through a handle, the scan's factory would build a second instance, so the library build reports it ([AWT197](../09-diagnostics.md#awt197)). When the library grants `InternalsVisibleTo` to any assembly, a container in such an assembly constructs everything itself and ignores the handles. The scans of such a library don't use handles. Such a library generates neither factory for an implementation that a scan matches too.

:::warning[Rebuild against each library version]

The handles and factories are public members of the library, and their signatures follow the internal types. Renaming an internal service or changing a constructor changes them. A container must be rebuilt against each new version of the library, even when the library's own public API didn't change.

:::

## Self-compiled scans

A `[Scan]` on a `[Module]` reaches internal implementations the same way: the module compiles its own scan **in its own build**, emitting a factory per match that constructs the implementation (which its own assembly can see) and returns the accessible interface. A consuming container reads each match like a container [`[Scan]`](./06-scanning.md) match, so a self-compiled scan behaves as close to a container scan as the assembly boundary allows, including the async initialization and disposal of a match it cannot see.

```csharp
public interface IClock;
public interface IPlugin;
public interface IRoaster;
internal sealed class Roaster(IClock clock) : IPlugin, IRoaster;   // stays internal

[Module]
[Scan<IPlugin>(As = ScanAs.MatchingInterface, Lifetime = Lifetime.Singleton)]
public static partial class PluginModule;   // partial, so the generator can add the factory
```

A consuming container `[Import]`s the module and resolves `IRoaster` without ever naming `Roaster`. Each match is registered like a container scan match: an explicit registration of the same service in the container (or another module) **overrides** it, as does the container's own `[Scan]`, and when several matches expose the **same** interface they **collect**: a `[Scan<IPlugin>(As = ScanAs.Marker)]` over two internal plug-ins resolves as `IEnumerable<IPlugin>`, exactly as it would on a container. Because diagnostics are reported in the *library's* build, it is the library author who sees any problem, not the consumer.

A match can also be a private type nested in the module, or in a class that contains the module. The module can build such a type, but other code in the assembly can't, so every container calls the module's factory, even a container in the same assembly. Its hooks can be private too. A private match that the scan registers as itself is skipped with a warning ([AWT196](../09-diagnostics.md#awt196)). So is a private match that the module also registers explicitly ([AWT193](../09-diagnostics.md#awt193)), because the type's single factory already serves that registration.

```csharp
[Module]
[Scan<IPlugin>(As = ScanAs.Marker)]
public static partial class TestPluginModule
{
    private sealed class FakePlugin : IPlugin;
}
```

The module must be static ([AWT152](../09-diagnostics.md#awt152)), `partial` ([AWT194](../09-diagnostics.md#awt194)) and non-generic ([AWT201](../09-diagnostics.md#awt201)), and its scan sweeps the module's own assembly only ([AWT202](../09-diagnostics.md#awt202)). This holds even when the library grants `InternalsVisibleTo`, because a container reads the matches from what the generator adds to the module. A few v1 limitations apply, each reported at the library's source. A match registers through exactly one accessible interface: a match with none is skipped with a warning ([AWT196](../09-diagnostics.md#awt196)), and one that would expose several is rejected ([AWT197](../09-diagnostics.md#awt197)), even when the exposures come from two different scans of the same module. For an `internal` module, an internal interface is accessible, and so is an internal match registered as itself, because only the module's assembly and the assemblies it grants `InternalsVisibleTo` can import it. Its hooks may take internal parameter types, and its conventions may be internal, for the same reason. Several matches under one interface still collect; what a self-compiled match cannot do is share a single instance across several interfaces the way a container scan can. A match that the module also registers and that every assembly can construct, because it and a constructor are public, gets no factory. A container constructs it for the registration and the scan alike, so they share one instance. A match's constructor parameters must be types a consumer can name ([AWT195](../09-diagnostics.md#awt195)). An `[Arg]` or `[FromKey]` on a parameter is copied onto the factory, but a match with an `[Inject]` property is rejected ([AWT200](../09-diagnostics.md#awt200)). `SkipUnconstructable` works as on a container scan: it travels with each generated registration, so a consumer that cannot satisfy a match's dependencies drops the match with a warning ([AWT141](../09-diagnostics.md#awt141)) instead of failing the build.

The same relaxation as for registrations applies when the library grants `InternalsVisibleTo` to any assembly. When the factory can't support a match ([AWT195](../09-diagnostics.md#awt195), [AWT197](../09-diagnostics.md#awt197) or [AWT200](../09-diagnostics.md#awt200)), a wrapper can't mirror the parameters of its non-generic hook ([AWT203](../09-diagnostics.md#awt203), [AWT204](../09-diagnostics.md#awt204)), or the module also registers the match, the module generates no factory for it and reports no error. It records the match, every service the scans expose it as, its lifetime and the reason. A container in an assembly that the library grants `InternalsVisibleTo` registers the match itself, exactly as the scan would, so one instance serves every service, and calls its hooks. A container in any other assembly reports [AWT231](../09-diagnostics.md#awt231) at its `[Import]`, with the reason. A match private to the module or to a class that contains it keeps its errors.

The expansion also stamps the module with a generated marker, present even when the scan matched nothing. A consuming container uses it as a version guard: importing a module whose metadata carries a `[Scan]` but no expansion, because the library was built without the Awaiten generator or with a version predating this feature, is an error ([AWT154](../09-diagnostics.md#awt154)) rather than a silent drop.

When the module lives in the **same assembly as the container**, there is no assembly boundary to bridge, and the container expands the module's `[Scan]` directly. The scan still means exactly what it means across assemblies: one accessible exposure per match, the same v1 limitations, and construction through the same constructor the generated factory would mirror, so moving a module between the container's project and a library never changes what its scan registers. A match skipped in the module's build (AWT196) stays skipped here too. The only difference is mechanical: the container constructs the match directly instead of calling the generated factory, which it cannot see within its own compilation, except for a match private to the module or a class that contains it, which it builds through the factory the module generates in the same build. The self-compiled factories are still generated into the module, so the same module keeps working for any *other* assembly that imports it.

### Shared instances

A match shares one instance with every other registration or scan match of the same implementation, in any assembly. This holds when the module registers the implementation through a `Factory`, when another module, the container or the base of an [extended container](./08-extending-containers.md) registers it, and when the scans of two modules match it. Each generated registration names its implementation, so a container in another assembly builds the instance once, through the registration that wins, and the match joins it. Types of the same full name from different assemblies are different implementations, so they never share an instance.

```csharp
public interface IAppliance;
public interface IBoiler;
internal sealed class Boiler : IAppliance, IBoiler;

[Module]
[Singleton<Boiler, IBoiler>]
public static partial class BoilerModule;

[Module]
[Scan<IAppliance>(As = ScanAs.Marker, Lifetime = Lifetime.Singleton)]
public static partial class ApplianceModule;   // a container that imports both shares one Boiler
```

The shared instance has the type the winning registration produces. Here that is `IBoiler`, the type the registration's factory returns, which doesn't convert to `IAppliance`. A container that can construct the implementation, because it is public or the library grants the container's assembly `InternalsVisibleTo`, then constructs it once for the registration and the match. Any other container reports [AWT240](../09-diagnostics.md#awt240) and drops the match, because the match's factory would build a second instance.

When another module builds the implementation through a [generated handle](#services-only-the-module-uses), the handle hides the instance. A container in another assembly then serves the match from the handle when that module also registers the implementation under the match's interface or one that converts to it, and otherwise reports [AWT240](../09-diagnostics.md#awt240) and drops the match.

## One level deep

Imports are not transitive. A module's own `[Import]` is not followed. Keep the container as the single place that composes modules. (A module's own `[Scan]`, by contrast, does reach the importing container: expanded directly by the container for a same-assembly module, or self-compiled in the module's build across assemblies, see above.)

:::note
Importing a type that is not a `[Module]` is an error ([AWT149](../09-diagnostics.md#awt149)), and a module must be static ([AWT152](../09-diagnostics.md#awt152)).
:::

## Where to go next

- [Scanning](./06-scanning.md) to register by convention instead of by hand.
- [Extending containers](./08-extending-containers.md) to reuse a whole container in tests and replace any inherited registration, not only a `Fallback` default.
- [Complete example](../02-complete-example.md) to see modules in a full container.
