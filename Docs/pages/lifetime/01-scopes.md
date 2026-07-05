# Scopes

A scope is a boundary for scoped services. Everything scoped is created once inside it and disposed when it closes. In the shop, each order is a scope. The tab and the order state belong to that order and go away when it is done.

## Open a scope

Call `CreateScope` on the root or on another scope. Dispose it when the unit of work is over.

```csharp
using var shop = new CoffeeShop.Root();

using (IAwaitenScope order = shop.CreateScope())
{
    Order o = order.Resolve<Order>();
    Tab tab = order.Resolve<Tab>();
} // the order, the tab, and any transients created here are disposed
```

## One instance per scope

Inside one scope, a scoped service resolves to the same instance every time. A different scope gets its own.

```csharp
using IAwaitenScope first = shop.CreateScope();
using IAwaitenScope second = shop.CreateScope();

Order a = first.Resolve<Order>();
Order b = first.Resolve<Order>();    // same as a
Order c = second.Resolve<Order>();   // a different order
```

## Singletons are shared, scopes are not

A child scope shares the container's singletons. It has its own scoped instances. Disposing a child does not touch the parent, and the espresso machine keeps running across every order.

## The root is the outermost scope

The `Root` is itself a scope. A scoped service resolved directly on the root behaves like a singleton, because the root lives as long as the container.

## Async scopes

If a scope has async scoped services, open it with `CreateScopeAsync` so they are initialized up front. See [Async initialization](../async-initialization).

```csharp
await using IAwaitenScope order = await shop.CreateScopeAsync(cancellationToken);
```

`CreateScopeAsync` warms the scope up front. Before it hands the scope back, it constructs every async-initialized scoped service and awaits its `InitializeAsync` in dependency order, so those services are ready on the first resolve. Plain scoped services are still created lazily on first use. If one of them fails to initialize, Awaiten disposes the half-built scope rather than leak it.

## Where to go next

- [Disposal](./disposal) for the teardown rules.
- [Lifetimes](../registration/lifetimes) for the difference between the three lifetimes.
