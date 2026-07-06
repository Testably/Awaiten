# Keyed dictionaries

A keyed dictionary hands you every keyed implementation of a service, mapped by its key. It is the milk fridge as a whole. Ask for the fridge and you can reach for oat, whole, or soy by name at runtime.

## Resolve the map

Register the implementations with keys, then depend on `IReadOnlyDictionary<string, TService>`.

```csharp
[Container]
[Singleton<OatMilk, IMilk>(Key = "Oat")]
[Singleton<WholeMilk, IMilk>(Key = "Whole")]
[Singleton<SoyMilk, IMilk>(Key = "Soy")]
public static partial class CoffeeShop;

public sealed class MilkFridge(IReadOnlyDictionary<string, IMilk> milks)
{
    public IMilk Get(string kind) => milks[kind];
}
```

You can also resolve it directly:

```csharp
var milks = shop.Resolve<IReadOnlyDictionary<string, IMilk>>();
IMilk oat = milks["Oat"];
```

## What goes in

The dictionary contains every keyed registration of the service, keyed by its `Key`. Unkeyed registrations are left out. Each member keeps its own lifetime. With no keyed registrations, you get an empty dictionary.

## The key type: string or enum

Awaiten synthesizes the dictionary for a `string` key or an `enum` key. For an enum-keyed map, register every implementation under a constant of the same enum and depend on `IReadOnlyDictionary<TEnum, TService>`:

```csharp
public enum MilkKind { Oat, Whole, Soy }

[Container]
[Singleton<OatMilk, IMilk>(Key = MilkKind.Oat)]
[Singleton<WholeMilk, IMilk>(Key = MilkKind.Whole)]
public static partial class CoffeeShop;

public sealed class MilkFridge(IReadOnlyDictionary<MilkKind, IMilk> milks);
```

The dictionary synthesizes only when every keyed registration of the service matches the requested key type. A key type that is neither `string` nor an enum, or one whose registrations mix key kinds, is a build error ([AWT159](../diagnostics#awt159)) unless you register the dictionary yourself. A synthesized dictionary already resolves every key, so applying `[FromKey]` to it makes no sense and is an error ([AWT160](../diagnostics#awt160)).

## The async form

For async members, ask for `Task<IReadOnlyDictionary<string, TService>>`. It awaits every member and keeps the consumer synchronously resolvable. If all members are synchronous, you get a completed task.

## Where to go next

- [Keyed services](../registration/keyed-services) to select a single keyed implementation.
- [Collections](./collections) for all unkeyed implementations.
