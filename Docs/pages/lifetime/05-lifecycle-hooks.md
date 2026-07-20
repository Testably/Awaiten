# Lifecycle hooks

Lifecycle hooks let you run code when an instance is activated and when it is released, without putting that code in the service itself. Calibrate the espresso machine when it comes online. Purge it when the shop closes.

## OnActivated and OnRelease

Name a hook method with `OnActivated` or `OnRelease`. The method lives on the container and takes the instance. Declare the parameter as the implementation type and Awaiten hands it over directly, no cast needed.

```csharp
[Container]
[Singleton<EspressoMachine>(OnActivated = nameof(Calibrate), OnRelease = nameof(Purge))]
public static partial class CoffeeShop
{
    private static void Calibrate(EspressoMachine machine) => machine.Calibrate();
    private static void Purge(EspressoMachine machine) => machine.Purge();
}
```

You can type the parameter as `object` instead when one hook serves several implementations.

`OnActivated` runs once, right after construction. For an async service it runs before `InitializeAsync`. `OnRelease` runs when the owner or scope is disposed, before the instance's own `Dispose`, and in reverse creation order. By default it runs *in addition to* that disposal; pair it with [`SuppressDisposal`](./disposal#suppressing-disposal) to replace disposal entirely, as when a hook returns an object to a pool.

## Hook parameters

Both hooks may declare parameters after the instance. The first parameter is always the instance; every parameter after it is resolved from the container graph, exactly like a constructor or factory parameter. Calibrate the machine from injected settings; return a pooled buffer to the pool it came from.

```csharp
[Container]
[Singleton<Settings>]
[Singleton<BufferPool>]
[Singleton<EspressoMachine>(OnActivated = nameof(Calibrate))]
[Transient<Buffer>(Factory = nameof(Rent), OnRelease = nameof(ReturnToPool))]
public static partial class CoffeeShop
{
    private static void Calibrate(EspressoMachine machine, Settings settings) => machine.Calibrate(settings);
    private static Buffer Rent(BufferPool pool) => pool.Rent();
    private static void ReturnToPool(Buffer buffer, BufferPool pool) => pool.Return(buffer);
}
```

An activation dependency is resolved inline as the hook is called. A release dependency is resolved at construction and captured by value into the queued closure, so the hook holds the instance it was queued for even if a later resolve fails and rolls back. Reverse creation-order teardown keeps a captured dependency (a singleton pool, say) alive until after the release that uses it has run.

Hook parameters participate in the graph like any other dependency: an unregistered one is [AWT101](../diagnostics#awt101), and they are covered by cycle, captive-dependency and async-taint analysis. A hook parameter cannot be a runtime `[Arg]` (there is no `Func<…>` call site to supply one), which is [AWT189](../diagnostics#awt189). A release hook parameter also cannot be a `Func<T>` or `Lazy<T>`: those capture a resolver delegate rather than a value, and by the time the hook runs the owner is already tearing down, so invoking the delegate would throw. That is [AWT191](../diagnostics#awt191); an activation hook may take them freely.

## Ordering and failure

Activation happens before initialization and before the instance is ever handed out, so a concurrent resolve never sees a service that has not been activated. If `OnActivated` throws, the instance is not published. A later resolve builds a fresh one and tries again. Release is only queued once activation succeeds.

## Eager singletons

By default a singleton is built the first time it is resolved. Mark it `Eager = true` to build it in the container's constructor instead, before any resolve, in registration order.

```csharp
[Singleton<EspressoMachine>(Eager = true)]
```

Combine `Eager` with async and `SyncResolveAfterInit`, and the singleton is constructed and initialized at build time. A plain eager singleton that is async-initialized cannot be built synchronously at construction, so that combination is a build error ([AWT161](../diagnostics#awt161)).

*Note: hooks are for services the container owns. Setting one on a pre-built `Instance` registration is an error ([AWT165](../diagnostics#awt165)). Conflicting coalesced directives across registrations of one implementation are caught too ([AWT166](../diagnostics#awt166)).*

## Hooks on a scan

A `[Scan]` can name the same `OnActivated`/`OnRelease` hooks, applied to every match, so a whole scanned family shares one routine. See [Scanning → Lifecycle hooks](../registration/scanning#lifecycle-hooks).

This holds for a `[Scan]` on a `[Module]` too, where a library keeps its implementations `internal` and exposes only interfaces. The hook method may stay `internal` alongside the implementation: the module resolves it in its own build and emits a `public` wrapper a consumer runs, so nothing internal leaks across the assembly boundary. The one added constraint is that the hook's parameters after the instance, resolved from the consuming container's graph, must be types the consumer can name. See [Scanning → Scan from a module](../registration/scanning#scan-from-a-module).

## Where to go next

- [Async initialization](../async-initialization) for `InitializeAsync`.
- [Disposal](./disposal) for teardown order.
- [Scanning](../registration/scanning#lifecycle-hooks) to apply a hook to a whole matched family.
