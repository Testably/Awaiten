# Disposal

Awaiten disposes what it creates. When a scope or the container closes, it tears down the instances it owns, in the right order. You clean the machine you used, and you clean it before you unplug the counter it sits on.

## Dispose the container

Disposing the `Root` disposes its singletons, in reverse creation order. Use `using`, or `await using` if anything is async-disposable.

```csharp
using (var shop = new CoffeeShop.Root())
{
    // ...
} // singletons disposed here
```

## Dispose a scope

Disposing a scope disposes its scoped instances and any transients it created, again in reverse order. It leaves the parent and the shared singletons alone.

```csharp
using (IAwaitenScope order = shop.CreateScope())
{
    // ...
} // this order's instances disposed; singletons untouched
```

## Who owns a transient

A transient is disposed by the owner that built it. Resolve one inside a scope and the scope disposes it. This is why [strict lifetime safety](./lifetime-safety) withholds disposable transients from the root by default. On the root they would pile up for the whole life of the container. To get a disposable transient with a bounded lifetime, use [Owned](./owned).

## What Awaiten does not dispose

An object you supplied with `Instance = nameof(...)` is yours. Awaiten never disposes it. The same is true for anything resolved from an external provider across the [MS.DI bridge](../msdi-bridge).

## Suppressing disposal

Sometimes the container builds a service but should not tear it down: the object is rented from a pool you return it to, or its lifetime belongs to something outside the container. Set `SuppressDisposal = true` and Awaiten constructs the service as usual but never calls its `Dispose`/`DisposeAsync`.

On its own this is opt-out ownership, the built counterpart of an `Instance`: the container makes the object but hands its teardown to you. Paired with an [`OnRelease` hook](./lifecycle-hooks) it becomes a clean return-to-pool, and with [`Owned<T>`](./owned) the rent-and-return is bounded to a `using` block.

```csharp
[Container]
[Transient<Cup>(Factory = nameof(RentCup), OnRelease = nameof(ReturnCup), SuppressDisposal = true)]
public static partial class CoffeeShop
{
    private static readonly CupPool Pool = new();

    private static Cup RentCup() => Pool.Rent();
    private static void ReturnCup(Cup cup) => Pool.Return(cup);   // returned, never disposed
}
```

`SuppressDisposal` applies to a service the container constructs, by constructor or `Factory`. Setting it on a pre-built `Instance`, which the container never owns or disposes anyway, is a build error ([AWT192](../diagnostics#awt192)).

An `OnRelease` hook counts like disposal for [strict lifetime safety](./lifetime-safety): each construction queues the release on the owner, so a release-hooked transient like `Cup` above is withheld from by-type resolution on the container root just as a disposable one is — on the root, every rented cup would be held until the container itself is disposed, and none would return to the pool before then. Rent through [`Owned<T>`](./owned) or from a child scope and the return-to-pool runs when the handle or scope is disposed. A `SuppressDisposal` service *without* a release hook tracks nothing at all, so it stays resolvable everywhere.

## Async disposal

Dispose with `await using` and Awaiten awaits `DisposeAsync` on every async-disposable instance it owns.

```csharp
await using var shop = new CoffeeShop.Root();
```

Disposing synchronously while the container owns a service that is `IAsyncDisposable` but not `IDisposable` throws at runtime, because a synchronous dispose cannot tear it down. Awaiten warns about this at compile time (AWT156).

## Using a disposed container throws

Resolving from a container or scope after it is disposed throws `ObjectDisposedException`. A disposed scope rejects even shared singletons.

## Where to go next

- [Owned](./owned) for disposable transients you rent and return.
- [Lifetime safety](./lifetime-safety) for how Awaiten prevents disposal leaks.
