# Property injection

Awaiten fills constructor parameters by default. Property injection is the opt-in alternative for the odd case where a constructor does not fit, like breaking a cycle between two services. It is done through an object initializer, so there is still no reflection.

## Opt in with `[Inject]`

Mark a settable or init property with `[Inject]`. Awaiten fills it after construction.

```csharp
public sealed class Barista
{
    [Inject] public IClock? Clock { get; set; }
}
```

A property without `[Inject]` is left alone. Injection is opt-in, one property at a time.

## Any dependency shape works

A property resolves the same way a constructor parameter does. That includes keys, relationships, collections, and keyed dictionaries.

```csharp
[Inject] [FromKey("Oat")] public IMilk? DefaultMilk { get; set; }
[Inject] public Func<Cup>? Cups { get; set; }
[Inject] public IEnumerable<ISyrup>? Syrups { get; set; }
```

## Break a cycle with `Deferred`

Two services that need each other cannot both be built first. Mark one side `Deferred = true`, and Awaiten caches the owner before wiring the property, so the cycle terminates.

```csharp
public sealed class OrderService
{
    [Inject(Deferred = true)] public InvoiceService? Invoice { get; set; }
}
```

A deferred property needs a plain `set`, not `init` ([AWT144](../diagnostics#awt144)). A cycle that cannot terminate, for example one made entirely of transients, is still a build error ([AWT145](../diagnostics#awt145)).

## Tolerate a missing service with `Optional`

By default an `[Inject]` property whose service is unregistered is a missing dependency ([AWT101](../diagnostics#awt101)). Mark it `Optional = true` to leave it at its default instead.

```csharp
[Inject(Optional = true)] public IMetricsSink? Metrics { get; set; }
```

*Note: an optional property should be settable, not init-only, or an unregistered dependency leaves it permanently at its default ([AWT158](../diagnostics#awt158)).*

## Where to go next

- [Relationships](./relationships) for deferred construction without property injection.
- [Resolving services](./resolving-services) for the resolve surface.
