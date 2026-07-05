# Runtime arguments

Some values are only known when you resolve, not when you register. The size of a cup depends on what the customer ordered. Mark those constructor parameters with `[Arg]`, and supply them through a `Func`.

## Mark a parameter with `[Arg]`

The rest of the constructor still resolves from the graph. Only the `[Arg]` parameters come from you.

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
