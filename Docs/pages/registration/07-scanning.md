# Scanning

Some sets of services grow over time. Every drink on the menu implements `IDrink`. You do not want to add a registration line each time you add a drink. A scan registers every type that matches a marker, and it does it at compile time by reading metadata, so there is still no runtime reflection.

## Scan for a marker

```csharp
[Container]
[Scan<IDrink>]
public static partial class CoffeeShop;
```

Every concrete, public, non-generic type assignable to `IDrink` is registered. The `typeof` form is equivalent:

```csharp
[Scan(typeof(IDrink))]
```

## Choose how they register

By default each match registers as itself. Use `As` to register under the marker interface instead, or both.

```csharp
[Scan<IDrink>(As = ScanAs.Marker)]
```

| `ScanAs` value | Registers each match as |
|---|---|
| `Self` (default) | The concrete type |
| `Marker` | The marker interface |
| `SelfAndMarker` | Both |

## Choose the lifetime

Scans register as transient unless you say otherwise.

```csharp
[Scan<IDrink>(Lifetime = AwaitenLifetime.Singleton)]
```

## Scan other assemblies

By default a scan looks in the container's own assembly. Point it at others with `InAssembliesOf`.

```csharp
[Scan(typeof(IDrink), InAssembliesOf = new[] { typeof(SeasonalDrinks) })]
```

## Open generic markers

An unbound generic marker matches closed forms, the way Autofac's closed-types-of works. Each match registers under its closed marker interface.

```csharp
[Scan(typeof(IView<>), As = ScanAs.Marker)]
```

## Overriding a scanned type

An explicit registration of a scanned type wins over the scan, so you can special-case one drink while scanning the rest.

*Note: a scan that matches nothing is a warning ([AWT138](../diagnostics#awt138)), not an error, so an empty menu does not break the build.*

## Where to go next

- [Modules](./modules) to group registrations for reuse.
- [Open generics](./open-generics) for generic service families.
