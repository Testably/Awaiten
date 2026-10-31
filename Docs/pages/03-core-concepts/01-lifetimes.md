---
description: Choose between singleton, scoped and transient lifetimes and see how Awaiten keeps them consistent.
---

# Lifetimes

A lifetime decides how long an instance lives and how often it is built. Awaiten has three, and you pick one per registration.

| Attribute | Built | Lives for |
|---|---|---|
| `[Singleton]` | Once | The whole container |
| `[Scoped]` | Once per scope | One scope |
| `[Transient]` | Every time | A single resolve |

Think of the shop. The espresso machine is shared by everyone, so it is a singleton. The running tab belongs to one order, so it is scoped. A cup is made fresh for each drink, so it is transient.

```csharp
[Container]
[Singleton<EspressoMachine>]
[Scoped<Order>]
[Transient<Cup>]
public static partial class CoffeeShop;
```

## Register against a service type

Pass the implementation first, then the service it provides. Consumers ask for the service, not the concrete type.

```csharp
[Singleton<EspressoBrewer, IBrewer>]
```

## Singleton

One instance for the container's life, shared everywhere, built exactly once even under concurrent resolves.

```csharp
EspressoMachine a = shop.Resolve<EspressoMachine>();
EspressoMachine b = shop.Resolve<EspressoMachine>();
// a and b are the same instance
```

## Scoped

One instance per scope. The same within a scope, different across scopes. See [Scopes](./02-scopes.md) for how to open one.

```csharp
using IAwaitenScope morning = shop.CreateScope();
using IAwaitenScope evening = shop.CreateScope();

Order a = morning.Resolve<Order>();
Order b = morning.Resolve<Order>();   // same as a
Order c = evening.Resolve<Order>();   // different order
```

The root is the outermost scope, so a scoped service resolved on the root is a singleton in practice.

## Transient

A fresh instance on every resolve. Nothing is cached.

```csharp
Cup first = shop.Resolve<Cup>();
Cup second = shop.Resolve<Cup>();     // a different cup
```

## One implementation, many services

Register the same type under several service types and Awaiten shares one instance across them.

```csharp
[Singleton<Ledger, IReader>]
[Singleton<Ledger, IWriter>]
// Resolve<IReader>() and Resolve<IWriter>() return the same Ledger
```

This holds for `Factory` and `Instance` registrations too. Name the same one on each service and they resolve to a single shared object. See [Factories and instances](../04-registration/01-factories-and-instances.md).

## Where the lifetime is set

A single registration picks its lifetime by its attribute: `[Singleton]`, `[Scoped]` or `[Transient]`. A bulk or composite registration, [`[Scan]`](../04-registration/06-scanning.md) and [`[Composite]`](../04-registration/03-composites.md), sets it with `Lifetime =` and defaults to `Transient`. A [decorator](../04-registration/02-decorators.md) has no lifetime of its own and inherits the lifetime of the service it wraps.

```csharp
[Singleton<EspressoMachine>]
[Scan<IDrink>(Lifetime = Lifetime.Singleton)]
[Composite<CompositeReceiptChannel, IReceiptChannel>]   // Transient
```

## Lifetimes must line up

A singleton lives forever, so whatever it holds lives that long too. Holding a transient is fine. The transient is built once, together with the singleton, and simply shares its lifetime. Holding a scoped dependency is not fine, because that would trap a per-order object inside a shop-wide one. Awaiten makes that a build error.

```csharp expect=AWT105
public sealed class EspressoMachine(Order order);   // singleton needs a scoped Order

[Container]
[Singleton<EspressoMachine>]
[Scoped<Order>]
public static partial class CoffeeShop;
```

```text
AWT105: singleton 'EspressoMachine' captures scoped 'Order'
```

If a singleton genuinely needs a scoped dependency, reach it through a relationship like `Func<T>`, which resolves it on demand instead of capturing it. See [Relationships](../05-resolution/02-relationships.md).

## Where to go next

- [Factories and instances](../04-registration/01-factories-and-instances.md) when a constructor is not enough.
- [Scopes](./02-scopes.md) to control scoped lifetimes.
- [Disposal](./04-disposal.md) for how instances are torn down.
