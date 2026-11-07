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

Because diagnostics are reported in the library's build, it is the library author who sees any problem. The module must be static ([AWT152](../09-diagnostics.md#awt152)), `partial` ([AWT194](../09-diagnostics.md#awt194)) and non-generic ([AWT201](../09-diagnostics.md#awt201)). The constructor's parameter types must be public and not private to the module or a class that contains it ([AWT195](../09-diagnostics.md#awt195)). An `[Arg]` or `[FromKey]` on a constructor parameter is copied onto the factory's parameter, so a container in another assembly passes the runtime argument or resolves the keyed service just as the constructor asks. An `[Inject]` property becomes a factory parameter with the property's name, and the factory assigns it. A property marked `Optional` or `Deferred` can't be a factory parameter ([AWT200](../09-diagnostics.md#awt200)). A hook's parameters must be public too ([AWT203](../09-diagnostics.md#awt203)) and carry no `[FromKey]` ([AWT204](../09-diagnostics.md#awt204)). A convention of the module that initializes the implementation must have a public type and method ([AWT234](../09-diagnostics.md#awt234)).

The factory returns one type, and every service of the implementation must convert from it, so that one instance serves them all. That type is the implementation when it's public. Otherwise it's a registered service that covers the others, or else a public base class or interface of the implementation that covers them. An internal implementation registered as `IGreeter` and as `IFormalGreeter : IGreeter` gets a factory that returns `IFormalGreeter`. Without a covering type, or when one of the module's scans also matches the implementation, it's rejected ([AWT197](../09-diagnostics.md#awt197)), because a second factory would split the instance a container shares.

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

## One level deep

Imports are not transitive. A module's own `[Import]` is not followed. Keep the container as the single place that composes modules. (A module's own `[Scan]`, by contrast, does reach the importing container: expanded directly by the container for a same-assembly module, or self-compiled in the module's build across assemblies, see above.)

:::note
Importing a type that is not a `[Module]` is an error ([AWT149](../09-diagnostics.md#awt149)), and a module must be static ([AWT152](../09-diagnostics.md#awt152)).
:::

## Where to go next

- [Scanning](./06-scanning.md) to register by convention instead of by hand.
- [Extending containers](./08-extending-containers.md) to reuse a whole container in tests and replace any inherited registration, not only a `Fallback` default.
- [Complete example](../02-complete-example.md) to see modules in a full container.
