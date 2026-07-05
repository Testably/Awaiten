# Keyed services

Sometimes one service type has several implementations and you want to pick a specific one by name. The milk fridge holds oat, whole, and soy. They are all `IMilk`, but a recipe asks for one in particular. Keys let you register them side by side and select the right one. When instead a single consumer needs its own implementation, use contextual binding, covered at the end of this page.

## Register with a key

Give each registration a `Key`.

```csharp
[Container]
[Singleton<OatMilk, IMilk>(Key = "Oat")]
[Singleton<WholeMilk, IMilk>(Key = "Whole")]
[Singleton<SoyMilk, IMilk>(Key = "Soy")]
public static partial class CoffeeShop;
```

## Select with `[FromKey]`

A consumer picks a keyed registration with `[FromKey]` on the parameter.

```csharp
public sealed class LatteRecipe([FromKey("Oat")] IMilk milk);
```

A keyed registration is reachable only through `[FromKey]`. A plain `Resolve<IMilk>()` will not return it. You can mix a keyed set with one unkeyed registration, and the unkeyed one is what a plain resolve gives you.

## Typed keys

A key can be an `enum` constant (or a `typeof(...)`) instead of a string. An enum key refactors safely, turns a typo into a compile error, and makes the valid keys discoverable.

```csharp
public enum MilkKind { Oat, Whole, Soy }

[Container]
[Singleton<OatMilk, IMilk>(Key = MilkKind.Oat)]
[Singleton<WholeMilk, IMilk>(Key = MilkKind.Whole)]
public static partial class CoffeeShop;

public sealed class LatteRecipe([FromKey(MilkKind.Oat)] IMilk milk);
```

Keys of different kinds stay distinct: the enum `MilkKind.Oat` and the string `"Oat"` are two different keys.

## Through relationships

`[FromKey]` works through `Func<T>` and `Lazy<T>` too, so a long-lived service can pull a keyed one on demand.

```csharp
public sealed class Barista(
    [FromKey("Oat")] Func<IMilk> oat,
    [FromKey("Whole")] Lazy<IMilk> whole);
```

## Want the whole set?

To get every keyed implementation as a map, ask for `IReadOnlyDictionary<string, IMilk>`. See [Keyed dictionaries](../resolution/keyed-dictionaries).

*Note: two registrations with the same service type and key collide ([AWT117](../diagnostics#awt117)).*

## Contextual binding

Keys pick an implementation at the call site. Contextual binding works from the other direction: you bind an implementation to a specific consumer, and that consumer gets it without asking for anything special. Add `WhenInjectedInto` and point it at the consumer type.

```csharp
public interface IReceiptPrinter;
public sealed class ThermalPrinter : IReceiptPrinter;
public sealed class WidePrinter : IReceiptPrinter;

public sealed class CounterRegister(IReceiptPrinter printer);
public sealed class DriveThroughRegister(IReceiptPrinter printer);

[Container]
[Singleton<ThermalPrinter, IReceiptPrinter>]
[Singleton<WidePrinter, IReceiptPrinter>(WhenInjectedInto = typeof(DriveThroughRegister))]
[Singleton<CounterRegister>]
[Singleton<DriveThroughRegister>]
public static partial class CoffeeShop;
```

`DriveThroughRegister` gets the `WidePrinter`, and its constructor still just asks for `IReceiptPrinter`. Every other consumer, and the public resolution, get the unconditional `ThermalPrinter`. The contextual registration is private to its consumer, so it is left out of the public collection too.

```csharp
shop.Resolve<DriveThroughRegister>().Printer; // WidePrinter
shop.Resolve<CounterRegister>().Printer;      // ThermalPrinter
shop.Resolve<IReceiptPrinter>();              // ThermalPrinter
```

A contextual binding fills an `[Inject]` property the same way it fills a constructor parameter, and a decorator on the service wraps the contextual implementation along with the default. If a parameter carries an explicit `[FromKey]`, the key wins, since it is the more specific choice.

*Note: if the named consumer has no unkeyed constructor parameter of the service to redirect, the binding never applies, and Awaiten warns ([AWT167](../diagnostics#awt167)).*

## Where to go next

- [Keyed dictionaries](../resolution/keyed-dictionaries) for the full key-to-instance map.
- [Collections](../resolution/collections) for all unkeyed implementations at once.
- [Context-aware factories](../resolution/context-aware-factories) to build a service based on who asked for it.
