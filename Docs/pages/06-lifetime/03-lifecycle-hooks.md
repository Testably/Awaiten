---
description: Run container-side code when an instance is activated or released, and build singletons eagerly.
---

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

`OnActivated` runs once, right after construction. For an async service it runs before the async initializer conventions. `OnRelease` runs when the owner or scope is disposed, before the instance's own `Dispose`, and in reverse creation order. By default it runs *in addition to* that disposal; pair it with [`SuppressDisposal`](../03-core-concepts/04-disposal.md#suppressing-disposal) to replace disposal entirely, as when a hook returns an object to a pool.

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

Hook parameters participate in the graph like any other dependency: an unregistered one is [AWT101](../09-diagnostics.md#awt101), and they are covered by cycle, captive-dependency and async-taint analysis. A hook parameter cannot be a runtime `[Arg]` (there is no `Func<…>` call site to supply one), which is [AWT189](../09-diagnostics.md#awt189). A release hook parameter also cannot be a `Func<T>` or `Lazy<T>`: those capture a resolver delegate rather than a value, and by the time the hook runs the owner is already tearing down, so invoking the delegate would throw. That is [AWT191](../09-diagnostics.md#awt191); an activation hook may take them freely.

## Asynchronous activation

An `OnActivated` hook may return `Task` or `ValueTask`. The container awaits it, so the service becomes [async-initialized](../03-core-concepts/03-async-initialization.md#use-an-activation-hook), like a service an async initializer convention matches. Connect the payment terminal without putting Awaiten on it.

```csharp
[Container]
[Singleton<Settings>]
[Singleton<PaymentTerminal, IPaymentTerminal>(OnActivated = nameof(ConnectAsync))]
public static partial class CoffeeShop
{
    private static Task ConnectAsync(PaymentTerminal terminal, Settings settings, CancellationToken cancellationToken)
        => terminal.HandshakeAsync(cancellationToken);
}
```

A `CancellationToken` parameter receives the token passed to `ResolveAsync`, `InitializeAsync` or `CreateScopeAsync`. Every other parameter after the instance resolves from the graph, as for a synchronous hook.

The service and everything that depends on it now resolve through `ResolveAsync`. All async rules apply: a synchronous `Func<T>` or `Lazy<T>` over it is [AWT119](../09-diagnostics.md#awt119), an `Eager` singleton needs `AsyncSafety.SyncAfterInit` ([AWT161](../09-diagnostics.md#awt161)), and the MS.DI bridge exposes it as `Task<T>`.

The hook works on every registration the container owns: any lifetime, keyed, `Factory`, open generic, scan and module registrations. A module or a base container runs it through a generated wrapper that returns the hook's task.

Only `Task` and `ValueTask` are awaited. A hook that returns `Task<T>` or `ValueTask<T>` is [AWT232](../09-diagnostics.md#awt232), because the container has no use for the result.

## Asynchronous release

An `OnRelease` hook may return `Task` or `ValueTask` too. Use it for cleanup that belongs to the container, not to the service, such as flushing a shared sink or returning an object to a pool. The service itself stays free of Awaiten.

```csharp
[Container]
[Singleton<ReceiptPrinter>]
[Scoped<Order>(OnRelease = nameof(PrintReceiptAsync))]
public static partial class CoffeeShop
{
    private static Task PrintReceiptAsync(Order order, ReceiptPrinter printer)
        => printer.PrintAsync(order);
}
```

`DisposeAsync` awaits the hook. It keeps the place a synchronous hook has: in reverse creation order, before any instance is disposed. Release dependencies are captured at construction, as for a synchronous hook. `DisposeAsync` has no `CancellationToken`, so the hook cannot take one ([AWT232](../09-diagnostics.md#awt232)).

The hook does not make the service async-initialized. It still resolves through `Resolve`, because the hook only runs at teardown.

Dispose the owner with `await using` or `DisposeAsync`. A synchronous `Dispose` cannot await the hook, so it skips it. It still runs every other release hook and disposes every instance, then throws an `InvalidOperationException` that names the service and, where it can, the hook, and tells you to use `DisposeAsync`. This is the same rule as for a service that is `IAsyncDisposable` but not `IDisposable`, and Awaiten warns about it at compile time ([AWT156](../09-diagnostics.md#awt156)). The same holds for an [`Owned<T>`](./01-owned.md) handle and for a scope of the [MS.DI bridge](../07-hosting/01-msdi-bridge.md).

A module or a base container runs an asynchronous release hook through a generated wrapper that returns the hook's task, like an activation hook.

## Ordering and failure

Activation happens before the instance is ever handed out, so a concurrent resolve never sees a service that has not been activated. The hook runs first, synchronous or asynchronous. Then the container awaits each [async initializer convention](../03-core-concepts/03-async-initialization.md#order-of-several-initializers) the service matches, in declaration order. If `OnActivated` or a convention throws or its task faults, the instance is not published, and a disposable instance is disposed. A later resolve builds a fresh one and tries again. Release is only queued once activation succeeds.

If `OnRelease` throws or its task faults, the teardown goes on: the other release hooks still run, and every instance is still disposed, including the one whose hook failed. The failure surfaces once the teardown is complete, together with any other teardown failure. See [Disposal → When teardown fails](../03-core-concepts/04-disposal.md#when-teardown-fails).

## Eager singletons

By default a singleton is built the first time it is resolved. Mark it `Eager = true` to build it in the container's constructor instead, before any resolve, in registration order.

```csharp
[Singleton<EspressoMachine>(Eager = true)]
```

An eager singleton is built as its consumers see it: when it is decorated, its decorators are built with it.

Combine `Eager` with async and `AsyncSafety.SyncAfterInit`, and the singleton is constructed and initialized at build time. A plain eager singleton that is async-initialized, or that has an async-initialized decorator, cannot be built synchronously at construction, so that combination is a build error ([AWT161](../09-diagnostics.md#awt161)).

:::note
Hooks are for services the container owns. Setting one on a pre-built `Instance` registration is an error ([AWT165](../09-diagnostics.md#awt165)). Conflicting coalesced directives across registrations of one implementation are caught too ([AWT166](../09-diagnostics.md#awt166)).
:::

## Hooks on a scan

A `[Scan]` can name the same `OnActivated`/`OnRelease` hooks, applied to every match, so a whole scanned family shares one routine. See [Scanning → Lifecycle hooks](../04-registration/06-scanning.md#lifecycle-hooks).

This holds for a `[Scan]` on a `[Module]` too, where a library keeps its implementations `internal` and exposes only interfaces. The hook method may stay `internal` alongside the implementation: the module resolves it in its own build and emits a `public` wrapper a consumer runs, so nothing internal leaks across the assembly boundary. The one added constraint is that the hook's parameters after the instance, resolved from the consuming container's graph, must be types the consumer can name. See [Scanning → Scan from a module](../04-registration/06-scanning.md#scan-from-a-module).

## Where to go next

- [Async initialization](../03-core-concepts/03-async-initialization.md) for async initializer conventions.
- [Disposal](../03-core-concepts/04-disposal.md) for teardown order.
- [Scanning](../04-registration/06-scanning.md#lifecycle-hooks) to apply a hook to a whole matched family.
