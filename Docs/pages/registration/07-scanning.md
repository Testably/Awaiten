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

A type the container cannot name is skipped rather than registered: an implementation that is `internal` to another assembly (with no `InternalsVisibleTo`), or a private nested class. Since a match quietly vanishing from the container is almost never what you meant, the scan reports the skip as [AWT193](../diagnostics#awt193). Widening the type to `public` brings it back. Only a type your scan actually asked for is reported, so the internal plumbing of a scanned assembly stays quiet, as does anything you filtered out. Whether the *interfaces* a match is exposed under are accessible is a separate question, covered below.

## Choose how they register

By default each match registers as itself. Use `As` to register under the marker interface instead, or both.

```csharp
[Scan<IDrink>(As = ScanAs.Marker)]
```

`ScanAs` is a `[Flags]` enum: the three exposures are independent and combine with `|`.

| `ScanAs` flag | Registers each match under |
|---|---|
| `Self` (default) | The concrete type |
| `Marker` | The marker interface |
| `MatchingInterface` | The interface named `I` + its own name (`Foo` → `IFoo`) |

```csharp
// Resolvable as itself, as a member of IEnumerable<IDrink>, and by its own IEspresso interface.
[Scan<IDrink>(As = ScanAs.Self | ScanAs.Marker | ScanAs.MatchingInterface)]
```

## Match the `Foo`/`IFoo` convention

`MatchingInterface` registers each match under the interface it implements whose name is `I` + the match's own name, the common .NET convention where `Foo` implements `IFoo`. Unlike a wide "as implemented interfaces" registration, it never binds a match to an incidental interface like `IDisposable`, so the composition graph stays legible.

```csharp
[Scan<IViewModel>(As = ScanAs.MatchingInterface)]
```

Here `MainViewModel : IViewModel, IMainViewModel` registers only under `IMainViewModel`. The match must genuinely implement the convention interface (it is selected from the type's implemented interfaces by name, not synthesized), and generic interfaces are not matched. A match that implements no such interface contributes no `MatchingInterface` registration; when the scan names a marker and the match would otherwise register nothing at all, that is a warning ([AWT182](../diagnostics#awt182)), or — when the interface is implemented but inaccessible to the container — a warning naming it ([AWT188](../diagnostics#awt188)). (Combined with `Self` or `Marker`, the other exposure still registers the match, so no warning is raised.) A match implementing several same-named interfaces with an own-namespace one prefers that one; with none, it registers under each, which a marker scan warns about ([AWT187](../diagnostics#awt187)).

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

## Scan without a marker

Some conventions have no shared marker at all: every `Foo` has its own `IFoo` and nothing else in common. The parameterless `[Scan]` matches every concrete type instead of a marker, narrowed by the same filters.

```csharp
[Scan(As = ScanAs.MatchingInterface, NamespacePatterns = ["MyApp.Services.**"])]
```

A markerless scan's `As` may not include `Marker` — there is no marker to register under — and it must carry at least one `NamePatterns`, `NamespacePatterns` or `InAssembliesOf` filter so it does not sweep every concrete type in scope. A wildcard-only pattern (`*`, `**.*`) does not count: it names nothing, so it does not narrow the sweep. Breaking either rule is an error ([AWT183](../diagnostics#awt183)). Because it scans broadly, a type that does not follow the convention is simply skipped rather than warned; if the scan ends up registering nothing at all, that is a warning ([AWT184](../diagnostics#awt184)).

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

## Scan from a module

A `[Scan]` may also sit on a `[Module]`, where it is compiled in the module's own build so a library can scan its `internal` implementations and expose them to consumers through their interfaces. See [self-compiled scans](./modules#self-compiled-scans).

## Where to go next

- [Modules](./modules) to group registrations for reuse.
- [Open generics](./open-generics) for generic service families.
