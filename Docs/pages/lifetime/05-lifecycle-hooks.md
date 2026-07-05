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

`OnActivated` runs once, right after construction. For an async service it runs before `InitializeAsync`. `OnRelease` runs when the owner or scope is disposed, before the instance's own `Dispose`, and in reverse creation order.

## Ordering and failure

Activation happens before initialization and before the instance is ever handed out, so a concurrent resolve never sees a service that has not been activated. If `OnActivated` throws, the instance is not published. A later resolve builds a fresh one and tries again. Release is only queued once activation succeeds.

## Eager singletons

By default a singleton is built the first time it is resolved. Mark it `Eager = true` to build it in the container's constructor instead, before any resolve, in registration order.

```csharp
[Singleton<EspressoMachine>(Eager = true)]
```

Combine `Eager` with async and `SyncResolveAfterInit`, and the singleton is constructed and initialized at build time. A plain eager singleton that is async-initialized cannot be built synchronously at construction, so that combination is a build error ([AWT161](../diagnostics#awt161)).

*Note: hooks are for services the container owns. Setting one on a pre-built `Instance` registration is an error ([AWT165](../diagnostics#awt165)). Conflicting coalesced directives across registrations of one implementation are caught too ([AWT166](../diagnostics#awt166)).*

## Where to go next

- [Async initialization](../async-initialization) for `InitializeAsync`.
- [Disposal](./disposal) for teardown order.
