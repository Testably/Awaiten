# Property injection

Awaiten fills constructor parameters by default. Property injection is the opt-in alternative for the odd case where a constructor does not fit, like breaking a cycle between two services. It is done through an object initializer, so there is still no reflection.

There are two ways to ask for it. Declaring it on the container with `[InjectProperty<T>]` keeps the service a plain class, with no Awaiten reference in your domain code. That is the idiom. Marking the property itself with `[Inject]` is the escape hatch for the one shape the container-side form cannot express.

## Declare it on the container with `[InjectProperty<T>]`

Point `[InjectProperty<TImplementation>]` at a settable property by name, on the container. Awaiten fills it after construction, exactly as if the property carried `[Inject]`, but the implementation stays a plain class.

```csharp
public sealed class Barista
{
    public IClock? Clock { get; set; }   // a plain property, no Awaiten attribute
}

[Container]
[Singleton<SystemClock, IClock>]
[Singleton<Barista>]
[InjectProperty<Barista>(nameof(Barista.Clock))]
public static partial class CoffeeShop;
```

Pass `nameof(Barista.Clock)` so a rename carries through. Repeat the attribute, once per property. It reads the *implementation* type, so it applies wherever that type is constructed, including a type pulled in by a [`[Scan]`](../registration/scanning) that a per-registration list of names could never reach.

## Any dependency shape works

A container-side property resolves the same way a constructor parameter does: relationships, collections, and keyed registrations all work. Each entry carries its own `Optional`, `Deferred`, and `Key`.

### Select a keyed registration with `Key`

```csharp
[InjectProperty<Barista>(nameof(Barista.DefaultMilk), Key = "Oat")]
```

`Key` takes a `string`, an `enum` value, or a `typeof(...)`, exactly like a [`[FromKey]`](../registration/keyed-services) on an `[Inject]` property.

### Break a cycle with `Deferred`

Two services that need each other cannot both be built first. Mark one side `Deferred = true`, and Awaiten caches the owner before wiring the property, so the cycle terminates.

```csharp
[InjectProperty<OrderService>(nameof(OrderService.Invoice), Deferred = true)]
```

A deferred property needs an accessible plain `set`, not `init` ([AWT144](../diagnostics#awt144)). A cycle that cannot terminate, for example one made entirely of transients, is still a build error ([AWT145](../diagnostics#awt145)).

### Tolerate a missing service with `Optional`

By default a property whose service is unregistered is a missing dependency ([AWT101](../diagnostics#awt101)). Mark the entry `Optional = true` to leave it at its default instead.

```csharp
[InjectProperty<Barista>(nameof(Barista.Metrics), Optional = true)]
```

*Note: a bad name, a read-only member, or a field is [AWT177](../diagnostics#awt177); an unreachable setter is [AWT136](../diagnostics#awt136); a duplicate entry for one property is [AWT179](../diagnostics#awt179); and a `Factory`/`Instance`-produced type, built whole by its source with nothing for the container to fill, is [AWT178](../diagnostics#awt178).*

A property without an entry is left alone. Injection stays opt-in, one property at a time, and if a property carries both `[Inject]` and an `[InjectProperty<T>]` entry the `[Inject]` member wins and the redundant entry is flagged ([AWT181](../diagnostics#awt181)).

## The escape hatch: `[Inject]` on the property

Marking the property itself with `[Inject]` asks for the same injection from the domain side. It fills exactly like a container-side entry, and `Optional`, `Deferred`, and a `[FromKey]` alongside it all work.

```csharp
public sealed class Handler<T>
{
    [Inject] public IClock? Clock { get; set; }
}
```

:::caution[Last resort]
`[Inject]` puts an Awaiten reference on your class. Reach for it only for the one shape `[InjectProperty<T>]` cannot express: an **open-generic** implementation like `Handler<T>` above, whose closings are only expanded on demand and so cannot be named by a closed type argument. For every closed type, prefer the container-side form above. Where the residual applies, `[Inject]` is the accepted trade, not a defect.
:::

:::caution[When not to reach for this]
Prefer constructor injection. A constructor parameter states a dependency plainly and cannot be forgotten; a property can be left unset. Reach for property injection only for a genuinely optional dependency with a sensible local default, or to break a cycle two constructors cannot. If you are using it to shorten a long constructor, that constructor is telling you the class does too much, see [Principles](../principles#when-power-becomes-a-smell).
:::

## Where to go next

- [Relationships](./relationships) for deferred construction without property injection.
- [Keyed services](../registration/keyed-services) for `[FromKey]` and container-side keyed selection.
- [Resolving services](./resolving-services) for the resolve surface.
