# Async initialization

Some services are not ready the moment they are constructed. An espresso machine has to heat its boiler. A payment terminal has to connect to the bank. This setup is asynchronous, and using the service before it finishes is a bug.

Awaiten treats this as a first-class concern. It tracks which services need async setup, drives that setup in the right order, and turns "you used it too early" into a compile error.

## Mark a service as async

Implement `IAsyncInitializable`. The container calls `InitializeAsync` exactly once per instance, after construction and before the service is handed out.

```csharp
public sealed class EspressoMachine : IAsyncInitializable
{
    public bool IsHot { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await HeatBoilerAsync(cancellationToken);
        IsHot = true;
    }
}

[Container]
[Singleton<EspressoMachine>]
public static partial class CoffeeShop;
```

A service that implements `IAsyncInitializable` is *async-tainted*. So is any service that depends on one. Awaiten follows the taint through the whole graph.

## Resolve it asynchronously

Use `ResolveAsync`. It constructs the service, awaits its initialization, and returns it ready to use.

```csharp
using var shop = new CoffeeShop.Root();

EspressoMachine machine = await shop.ResolveAsync<EspressoMachine>(cancellationToken);
// machine.IsHot is true
```

A singleton is initialized exactly once, even under concurrent resolves. Every caller gets the same warmed instance.

## Warm everything up front

`InitializeAsync` on the root warms every async singleton ahead of time, in dependency order. A dependency is always initialized before the service that needs it. The call is idempotent and thread-safe, so calling it twice initializes nothing twice.

```csharp
await using var shop = new CoffeeShop.Root();
await shop.InitializeAsync(cancellationToken);   // boiler hot, terminal connected

// later, on the hot path, resolves are ready immediately
```

This is the usual startup pattern. Warm up once when the shop opens, then serve.

## Warm a scope

`CreateScopeAsync` opens a scope and initializes its async scoped services eagerly. If one of them throws during initialization, the half-built scope is disposed for you rather than leaked.

```csharp
await using IAwaitenScope order = await shop.CreateScopeAsync(cancellationToken);
```

## Reaching an async service synchronously is a compile error

This is the point of the whole feature. In the strict default, you cannot get an async-tainted service through a synchronous path.

```csharp
EspressoMachine machine = shop.Resolve<EspressoMachine>();   // build error
```

> ```
> AWT119: 'EspressoMachine' is async-initialized. Resolve it with ResolveAsync.
> ```

If you do reach it at runtime through the by-type API, `Resolve` throws with guidance toward `ResolveAsync`, and `TryResolve` reports it as not available. It never hands back an uninitialized instance.

The same rule holds for a service that is only *transitively* tainted. A `Barista` that depends on the `EspressoMachine` is async too, so it is resolved with `ResolveAsync`.

## Deferred shapes launder the taint

A synchronous service can still hold an async one, as long as it defers the work. Ask for `Task<T>`, `Func<Task<T>>`, or `Lazy<Task<T>>`, and the consumer stays synchronously resolvable.

```csharp
public sealed class Barista(
    Func<Task<EspressoMachine>> machine,   // pulled and awaited on demand
    Task<EspressoMachine> warming,         // started for you, awaited later
    Lazy<Task<EspressoMachine>> lazy)       // built once, on first use
{
    public async Task<Cup> PullShotAsync() => (await machine()).Pull();
}

// Barista itself resolves synchronously:
Barista barista = shop.Resolve<Barista>();
```

Each shape still delivers a fully initialized machine when you await it.

## Async factories

A `Factory` method may return `Task<T>`. Awaiten awaits it, and if the result is also `IAsyncInitializable`, it awaits `InitializeAsync` after. Add a `CancellationToken` parameter and the resolve-time token flows straight into it.

```csharp
[Container]
[Singleton<IPaymentTerminal>(Factory = nameof(ConnectAsync))]
public static partial class CoffeeShop
{
    private static async Task<IPaymentTerminal> ConnectAsync(CancellationToken cancellationToken)
    {
        var terminal = new PaymentTerminal();
        await terminal.HandshakeAsync(cancellationToken);
        return terminal;
    }
}
```

## Failures are not cached

If initialization throws, the faulted result is not memoized. The next resolve builds and initializes a fresh instance. A cancellation from one caller does not poison the shared singleton either. A later caller with a live token can still initialize it.

## Relax the rule when you need to

Set `SyncResolveAfterInit = true` on the container to allow synchronous resolution of async services.

```csharp
[Container(SyncResolveAfterInit = true)]
[Singleton<EspressoMachine>]
public static partial class CoffeeShop;
```

After `InitializeAsync` warms a singleton, `Resolve` returns that same warmed instance. If you resolve before warm-up, the synchronous path delegates to the memoizing async path, so you still get one instance, initialized exactly once, never a second uninitialized one. Use this when a framework forces a synchronous resolve on you.

:::warning[This can block]
A synchronous resolve of an async service that has not been warmed yet blocks the calling thread until its `InitializeAsync` finishes, because the sync path waits on the async one. Once `InitializeAsync` has warmed it, the resolve is just a cache read and does not block. So warm up at startup, and avoid a cold synchronous resolve on a thread that cannot afford to wait, or on one with a synchronization context, where blocking on async work can deadlock.
:::

## Async disposal

An async service is often async-disposable too. Dispose the container or scope with `await using`, and Awaiten awaits `DisposeAsync` on everything it owns.

```csharp
await using var shop = new CoffeeShop.Root();
```

Disposing a container synchronously while it owns an `IAsyncDisposable`-only service throws at runtime, and Awaiten warns about it at compile time ([AWT156](./diagnostics#awt156)). See [Disposal](./lifetime/disposal).

## Where to go next

- [Relationships](./resolution/relationships) for `Func`, `Lazy`, and `Task` shapes.
- [Owned](./lifetime/owned) for async disposable transients you rent and return.
- [MS.DI bridge](./msdi-bridge) to run async initialization under the generic host.
