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

A library often keeps its implementations `internal` and exposes only interfaces. A consuming container cannot construct an inaccessible type, so it could never register one — unless the library hand-wrote a factory per type. A `[Scan]` on a `[Module]` closes that gap: the module compiles its own scan **in its own build**, emitting a factory per match that constructs the implementation (which its own assembly can see) and returns the accessible interface. To the consumer this is an ordinary module factory registration, so nothing new crosses the assembly boundary.

```csharp
public interface IClock;
public interface IRoaster;
internal sealed class Roaster(IClock clock) : IPlugin, IRoaster;   // stays internal

[Module]
[Scan<IPlugin>(As = ScanAs.MatchingInterface, Lifetime = AwaitenLifetime.Singleton)]
public static partial class PluginModule;   // partial, so the generator can add the factory
```

A consuming container `[Import]`s the module and resolves `IRoaster` without ever naming `Roaster`. The generated registration is an overridable default (`Fallback.Silent`), so the container can replace it with its own registration. Because diagnostics are reported in the *library's* build, the library author — not the consumer — sees any problem.

The module must be `partial` ([AWT194](../diagnostics#awt194)). A few v1 limitations apply, each reported at the library's source: a match exposes through exactly one accessible interface ([AWT196](../diagnostics#awt196)/[AWT197](../diagnostics#awt197)), and its constructor parameters must be types a consumer can name ([AWT195](../diagnostics#awt195)). Self-compilation is a cross-assembly feature: within a single assembly the container can already `[Scan]` its own `internal` types directly.

## One level deep

Imports are not transitive. A module's own `[Import]` is not followed. Keep the container as the single place that composes modules. (A module's own `[Scan]`, by contrast, is self-compiled in the module's build — see above — not collected by the importing container.)

*Note: importing a type that is not a `[Module]` is an error ([AWT149](../diagnostics#awt149)), and a module must be static ([AWT152](../diagnostics#awt152)).*

## Where to go next

- [Scanning](./scanning) to register by convention instead of by hand.
- [Complete example](../complete-example) to see modules in a full container.
