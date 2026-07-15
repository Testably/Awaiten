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

## Narrow the matches

Marker assignability is often wider than you want. Three optional filters narrow it, combined with AND:

```csharp
[Scan<IDrink>(
    NamePatterns = ["*Latte", "!Decaf*"],                      // ends in Latte, but not the Decaf ones
    NamespacePatterns = ["CoffeeShop.Menu.**", "!**.Tests"],   // under Menu, excluding test namespaces
    Exclude = [typeof(DiscontinuedFlatWhite)])]                // and never this exact type
```

- **`NamePatterns`** globs the simple type name. `*` matches any run of characters, so `"*Latte"` is ends-with, `"Iced*"` starts-with, `"*Pumpkin*"` contains, and `"Latte"` an exact name.
- **`NamespacePatterns`** globs the namespace and is segment-aware on `.`: `*` matches one segment, `**` matches zero or more. So `"CoffeeShop.Menu.**"` is that namespace and everything nested beneath it (but not the sibling `CoffeeShop.MenuLegacy`), `"CoffeeShop.Menu.*"` its immediate children only, and `"**.Tests"` any namespace ending in a `Tests` segment.
- **`Exclude`** drops types by exact identity, so an entry survives a rename and never removes a same-named type elsewhere.

In either pattern list a bare entry includes and a `!`-prefixed entry excludes; a candidate passes an axis when it matches some include (or the list gives none) and no exclude. Matching is ordinal (case-sensitive). A filter set that removes every match warns with [AWT172](../diagnostics#awt172), a never-applied exclusion with [AWT173](../diagnostics#awt173), and a match-everything include (`*` or `**`) with [AWT174](../diagnostics#awt174).

## Open generic markers

An unbound generic marker matches closed forms, the way Autofac's closed-types-of works. Each match registers under its closed marker interface.

```csharp
[Scan(typeof(IView<>), As = ScanAs.Marker)]
```

## Overriding a scanned type

An explicit registration of a scanned type wins over the scan, so you can special-case one drink while scanning the rest.

*Note: a scan that matches nothing is a warning ([AWT138](../diagnostics#awt138)), not an error, so an empty menu does not break the build.*

:::caution[When not to reach for this]
Prefer explicit registrations. A scan trades away the property that makes the composition root useful: the whole graph visible in one place. Reach for a scan only for a large, uniform family that grows on its own, like message handlers, validators, or plug-ins, where listing each one adds churn without adding clarity. For a handful of services, spell them out, see [Design principles](../design-principles#when-power-becomes-a-smell).
:::

## Where to go next

- [Modules](./modules) to group registrations for reuse.
- [Open generics](./open-generics) for generic service families.
