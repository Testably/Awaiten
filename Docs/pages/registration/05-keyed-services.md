# Keyed services

Sometimes one service type has several implementations and you want to pick a specific one by name. The milk fridge holds oat, whole, and soy. They are all `IMilk`, but a recipe asks for one in particular. Keys let you register them side by side, and the container picks the right one for each consumer, so the consumer stays a plain class.

## Register with a key

Give each registration a `Key`.

```csharp
[Container]
[Singleton<OatMilk, IMilk>(Key = "Oat")]
[Singleton<WholeMilk, IMilk>(Key = "Whole")]
[Singleton<SoyMilk, IMilk>(Key = "Soy")]
public static partial class CoffeeShop;
```

A plain `Resolve<IMilk>()` does not return a keyed registration. You can mix a keyed set with one unkeyed registration, and the unkeyed one is what a plain resolve gives you.

### Typed keys

A key can be an `enum` constant (or a `typeof(...)`) instead of a string. An enum key refactors safely, turns a typo into a compile error, and makes the valid keys discoverable.

```csharp
public enum MilkKind { Oat, Whole, Soy }

[Container]
[Singleton<OatMilk, IMilk>(Key = MilkKind.Oat)]
[Singleton<WholeMilk, IMilk>(Key = MilkKind.Whole)]
public static partial class CoffeeShop;
```

Keys of different kinds stay distinct: the enum `MilkKind.Oat` and the string `"Oat"` are two different keys.

## Bind a variant to a consumer with `WhenInjectedInto`

When a consumer should always get one particular implementation, bind it at the container with `WhenInjectedInto` and point it at the consumer type. The consumer's constructor just asks for the bare service type; the container decides which implementation it gets.

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

A contextual binding fills a property injected by [`[InjectProperty<T>]`](../resolution/property-injection) the same way it fills a constructor parameter, and a decorator on the service wraps the contextual implementation along with the default.

*Note: if the named consumer has no unkeyed constructor parameter of the service to redirect, the binding never applies, and Awaiten warns ([AWT167](../diagnostics#awt167)).*

## Disambiguate same-typed dependencies with a factory

`WhenInjectedInto` redirects a single parameter, so it cannot express a consumer that needs *two* of the same type under different keys. Build that type through a [factory method](./factories-and-instances) whose parameters carry the keys, so the disambiguation lives on the factory and the type stays a plain class.

```csharp
public sealed class Blend(IMilk primary, IMilk secondary);   // a plain class: two same-typed params, no attributes

[Container]
[Singleton<OatMilk, IMilk>(Key = "Oat")]
[Singleton<SoyMilk, IMilk>(Key = "Soy")]
[Transient<Blend>(Factory = nameof(MakeBlend))]
public static partial class CoffeeShop
{
    private static Blend MakeBlend([FromKey("Oat")] IMilk primary, [FromKey("Soy")] IMilk secondary)
        => new(primary, secondary);
}
```

The `[FromKey]` on each factory parameter selects the keyed registration, whether a string key or a typed `MilkKind.Oat`, and `Blend` never mentions Awaiten. A factory parameter can also be a relationship such as `[FromKey("Oat")] Func<IMilk>` or `[FromKey("Oat")] Lazy<IMilk>` to pull a keyed implementation on demand, and it takes an [`[Arg]`](../resolution/runtime-arguments) the same way, for a value supplied at resolve time.

To pick a keyed registration imperatively, outside any constructor, pass the key to `Resolve`. See [keyed resolution](../resolution/resolving-services#keyed-resolution). That reads the container directly, so it puts no attribute on a domain type.

## Select directly with `[FromKey]`

A consumer can also name the key itself, with `[FromKey]` on the parameter. It is the most direct form and composes through `Func<T>` and `Lazy<T>`, but it names the key inside the domain type.

```csharp
public sealed class LatteRecipe([FromKey("Oat")] IMilk milk);
```

A typed key works the same way, and the enum `MilkKind.Oat` and the string `"Oat"` stay distinct keys:

```csharp
public sealed class LatteRecipe([FromKey(MilkKind.Oat)] IMilk milk);
```

:::caution[Last resort]
`[FromKey]` on a constructor parameter couples the domain type to Awaiten. The composition-root forms above express the same selection without that coupling: `WhenInjectedInto` when a consumer always wants one variant, and a factory-`[FromKey]` when it takes two same-typed dependencies under different keys. Reach for the direct form only when you would rather annotate the constructor than add a factory; it is an accepted trade, not a defect.
:::

## Want the whole set?

To get every keyed implementation as a map, ask for `IReadOnlyDictionary<string, IMilk>`. See [Keyed dictionaries](../resolution/keyed-dictionaries).

*Note: two registrations with the same service type and key collide ([AWT117](../diagnostics#awt117)).*

:::tip[Keeping DI out of your domain]
`WhenInjectedInto` and factory-`[FromKey]` keep domain code free of any Awaiten reference, because the selection lives on the container and the consumer uses no attributes at all. Prefer them; the direct `[FromKey]` above is the escape hatch when you deliberately trade that separation for a more compact constructor.
:::

## Where to go next

- [Keyed dictionaries](../resolution/keyed-dictionaries) for the full key-to-instance map.
- [Collections](../resolution/collections) for all unkeyed implementations at once.
- [Context-aware factories](../resolution/context-aware-factories) to build a service based on who asked for it.
