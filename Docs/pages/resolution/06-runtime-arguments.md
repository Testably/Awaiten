# Runtime arguments

Some values are only known when you resolve, not when you register. The size of a cup depends on what the customer ordered. A `[Arg]` parameter is filled from your call instead of the graph, and you supply it through a `Func`. Put the `[Arg]` on a container factory method to keep the produced type a plain class, or on the constructor directly when you would rather not add a factory.

## Keep the argument off the domain type with a factory

Put `[Arg]` on a container factory-method parameter. The produced type keeps a plain constructor; the runtime argument is forwarded to the factory, while the rest of the parameters still resolve from the graph, and you supply the argument through the same `Func`.

```csharp
public sealed class Cup(EspressoMachine machine, string size)   // a plain class, no attributes
{
    public string Size { get; } = size;
}

[Container]
[Singleton<EspressoMachine>]
[Transient<Cup>(Factory = nameof(MakeCup))]
public static partial class CoffeeShop
{
    private static Cup MakeCup(EspressoMachine machine, [Arg] string size) => new(machine, size);
}
```

`shop.Resolve<Func<string, Cup>>()("Large")` builds a large cup, and `Cup` never mentions Awaiten.

## Mark a parameter with `[Arg]`

You can also mark the constructor parameter directly. The rest of the constructor still resolves from the graph; only the `[Arg]` parameters come from you.

```csharp
public sealed class Cup
{
    public Cup(EspressoMachine machine, [Arg] string size)
    {
        Size = size;
    }

    public string Size { get; }
}

[Container]
[Singleton<EspressoMachine>]
[Transient<Cup>]
public static partial class CoffeeShop;
```

:::caution[Last resort]
`[Arg]` on a constructor parameter couples the domain type to Awaiten. The factory form above keeps the produced type plain. Reach for the direct form only for a runtime argument you would rather supply on the constructor than route through a factory; it is an accepted trade, not a defect.
:::

## Supply the value through a Func

A service with `[Arg]` parameters is resolved through a matching `Func<TArg..., T>`. The arguments are matched positionally.

```csharp
Func<string, Cup> makeCup = shop.Resolve<Func<string, Cup>>();
Cup large = makeCup("Large");
```

A consumer usually injects that `Func`:

```csharp
public sealed class Barista(Func<string, Cup> cups)
{
    public Cup Serve(string size) => cups(size);
}
```

## Several arguments

List every `[Arg]` and pass a `Func` with the matching shape. Order follows the constructor.

```csharp
public sealed class Receipt(Store store, [Arg] string customer, [Arg] int number);

// Resolve<Func<string, int, Receipt>>()
```

## Rules

A service with `[Arg]` parameters must be transient ([AWT114](../diagnostics#awt114)), because each call builds a fresh instance from your arguments. It is reachable only through a `Func<TArg..., T>`, not as a plain, `Lazy<T>`, or `Task<T>` dependency ([AWT115](../diagnostics#awt115)). If the argument shape does not match the `[Arg]` parameters, that is a build error too ([AWT113](../diagnostics#awt113)).

The argument wins even when its type is itself registered. A `[Arg] string` always comes from your call, never from the graph.

## Where to go next

- [Relationships](./relationships) for `Func<T>` without arguments.
- [Owned](../lifetime/owned) for `Func<TArg, Owned<T>>` when the built service is disposable.
