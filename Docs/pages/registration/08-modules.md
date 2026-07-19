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

A module can offer a default that the container is free to replace. Mark it `Fallback = Fallback.Warn`. If the container registers the same service, its own registration wins and the default is dropped entirely, even from collections. Use `Fallback = Fallback.Silent` for a registration that contributes only when nothing else provides the service; unlike `Warn`, it stays quiet when another overridable default competes with it (`Warn` reports [AWT148](../diagnostics#awt148) in that case).

```csharp
[Module]
[Singleton<RealTimeSystem, ITimeSystem>(Fallback = Fallback.Warn)]
public static class InfrastructureModule;

[Container]
[Import(typeof(InfrastructureModule))]
[Singleton<MockTimeSystem, ITimeSystem>]   // wins over the module default
public static partial class CoffeeShop;
```

## Factories and instances in modules

A module can use `Factory` and `Instance` too. Awaiten calls the members qualified by the module type, so this works across assemblies.

```csharp
[Module]
[Singleton<MemoryCache, ICache>(Instance = nameof(Cache))]
public static class ProductionModule
{
    public static MemoryCache Cache { get; } = new();
}
```

## Self-compiled scans

A library often keeps its implementations `internal` and exposes only interfaces. A consuming container cannot construct an inaccessible type, so it could never register one — unless the library hand-wrote a factory per type. A `[Scan]` on a `[Module]` closes that gap: the module compiles its own scan **in its own build**, emitting a factory per match that constructs the implementation (which its own assembly can see) and returns the accessible interface. A consuming container reads each match like a container [`[Scan]`](./scanning) match, so a self-compiled scan behaves as close to a container scan as the assembly boundary allows.

```csharp
public interface IClock;
public interface IPlugin;
public interface IRoaster;
internal sealed class Roaster(IClock clock) : IPlugin, IRoaster;   // stays internal

[Module]
[Scan<IPlugin>(As = ScanAs.MatchingInterface, Lifetime = AwaitenLifetime.Singleton)]
public static partial class PluginModule;   // partial, so the generator can add the factory
```

A consuming container `[Import]`s the module and resolves `IRoaster` without ever naming `Roaster`. Each match is registered like a container scan match: an explicit registration of the same service in the container (or another module) **overrides** it, and when several matches expose the **same** interface they **collect** — a `[Scan<IPlugin>(As = ScanAs.Marker)]` over two internal plug-ins resolves as `IEnumerable<IPlugin>`, exactly as it would on a container. Because diagnostics are reported in the *library's* build, the library author — not the consumer — sees any problem.

The module must be static ([AWT152](../diagnostics#awt152)), `partial` ([AWT194](../diagnostics#awt194)) and non-generic ([AWT201](../diagnostics#awt201)), and its scan sweeps the module's own assembly only ([AWT202](../diagnostics#awt202)). A few v1 limitations apply, each reported at the library's source. A match registers through exactly one accessible interface: a match with none is skipped with a warning ([AWT196](../diagnostics#awt196)), and one that would expose several is rejected ([AWT197](../diagnostics#awt197)), even when the exposures come from two different scans of the same module. Several matches under one interface still collect; what a self-compiled match cannot do is share a single instance across several interfaces the way a container scan can. A match's constructor parameters must be types a consumer can name ([AWT195](../diagnostics#awt195)), without `[Inject]`/`[Arg]` metadata the factory could not mirror ([AWT200](../diagnostics#awt200)). `SkipUnconstructable` works as on a container scan: it travels with each generated registration, so a consumer that cannot satisfy a match's dependencies drops the match with a warning ([AWT141](../diagnostics#awt141)) instead of failing the build.

The expansion also stamps the module with a generated marker, present even when the scan matched nothing. A consuming container uses it as a version guard: importing a module whose metadata carries a `[Scan]` but no expansion, because the library was built without the Awaiten generator or with a version predating this feature, is an error ([AWT154](../diagnostics#awt154)) rather than a silent drop.

When the module lives in the **same assembly as the container**, there is no assembly boundary to bridge: the container expands the module's `[Scan]` directly, exactly as if it were declared on the container itself, with full container-scan semantics (multi-interface exposure, direct construction of `internal` matches). The self-compiled factories are still generated into the module, so the same module keeps working for any *other* assembly that imports it; but note that the v1 limitations above are checked in the module's build regardless, since the module cannot know whether a cross-assembly consumer exists.

## One level deep

Imports are not transitive. A module's own `[Import]` is not followed. Keep the container as the single place that composes modules. (A module's own `[Scan]`, by contrast, does reach the importing container: expanded directly by the container for a same-assembly module, or self-compiled in the module's build across assemblies — see above.)

*Note: importing a type that is not a `[Module]` is an error ([AWT149](../diagnostics#awt149)), and a module must be static ([AWT152](../diagnostics#awt152)).*

## Where to go next

- [Scanning](./scanning) to register by convention instead of by hand.
- [Complete example](../complete-example) to see modules in a full container.
