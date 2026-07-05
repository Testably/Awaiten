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
[Singleton<SystemClock, IClock>(Fallback = Fallback.Warn)]
public static class InfrastructureModule;

[Container]
[Import(typeof(InfrastructureModule))]
[Singleton<TestClock, IClock>]           // wins over the module default
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

## One level deep

Imports are not transitive. A module's own `[Import]` is not followed, and a `[Scan]` inside a module is not collected. Keep the container as the single place that composes modules.

*Note: importing a type that is not a `[Module]` is an error ([AWT149](../diagnostics#awt149)), and a module must be static ([AWT152](../diagnostics#awt152)).*

## Where to go next

- [Scanning](./scanning) to register by convention instead of by hand.
- [Complete example](../complete-example) to see modules in a full container.
