# Lifetime safety

Disposable transients are easy to leak. Resolve one on a long-lived owner and it sits there, undisposed, for the life of that owner. Awaiten guards against this by default, and the guard is a compile-time decision, not a runtime surprise.

## Strict is the default

In the strict default, a disposable transient is withheld from by-type resolution on the root. On the root, it would accumulate for the whole life of the container.

```csharp
[Container]            // strict by default
[Transient<BrewSession>]   // BrewSession is IDisposable
public static partial class CoffeeShop;

BrewSession session = shop.Resolve<BrewSession>();   // withheld
```

At runtime this `Resolve` throws with guidance, and `TryResolve` returns `false`. The message steers you toward a bounded option.

## What is still allowed

Strict mode does not get in your way for the safe shapes:

- `Owned<T>` and `Func<Owned<T>>`, because you control disposal. See [Owned](./owned).
- A disposable transient injected into a normal constructor, because its owner disposes it.
- A disposable transient resolved from a child scope, because the scope bounds its lifetime.
- A singleton's `Lazy<T>` over a disposable, because it is memoized and bounded.

What strict withholds is the leak-prone shape: a plain `Func<T>` or `Func<TArg, T>` over a disposable, held by a root-owned service. That is a non-suppressible error ([AWT118](../diagnostics#awt118)), because each call would strand another undisposed instance on the root. Under `Loose` it relaxes to a suppressible warning.

## Loose when you want MS.DI semantics

Set `LifetimeSafety = LifetimeSafety.Loose` to make everything resolvable everywhere, the way Microsoft's container behaves. The leak-prone pattern becomes a warning instead of being withheld.

```csharp
[Container(LifetimeSafety = LifetimeSafety.Loose)]
[Transient<BrewSession>]
public static partial class CoffeeShop;
```

Reach for `Loose` when you are bridging to code that expects MS.DI behavior. Otherwise, strict plus `Owned<T>` keeps disposal honest.

## Where to go next

- [Owned](./owned) for the recommended way to rent disposables.
- [Disposal](./disposal) for the teardown rules.
- [MS.DI bridge](../msdi-bridge) for interop with Microsoft's container.
