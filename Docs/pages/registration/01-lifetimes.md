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

One instance per scope. The same within a scope, different across scopes. See [Scopes](../lifetime/scopes) for how to open one.

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

This holds for `Factory` and `Instance` registrations too. Name the same one on each service and they resolve to a single shared object. See [Factories and instances](./factories-and-instances).

## Lifetimes must line up

A singleton lives forever, so whatever it holds lives that long too. Holding a transient is fine. The transient is built once, together with the singleton, and simply shares its lifetime. Holding a scoped dependency is not fine, because that would trap a per-order object inside a shop-wide one. Awaiten makes that a build error.

```csharp
public sealed class EspressoMachine(Order order);   // singleton needs a scoped Order

[Container]
[Singleton<EspressoMachine>]
[Scoped<Order>]
public static partial class CoffeeShop;
```

> ```
> AWT105: singleton 'EspressoMachine' captures scoped 'Order'
> ```

If a singleton genuinely needs a scoped dependency, reach it through a relationship like `Func<T>`, which resolves it on demand instead of capturing it. See [Relationships](../resolution/relationships).

## Where to go next

- [Factories and instances](./factories-and-instances) when a constructor is not enough.
- [Scopes](../lifetime/scopes) to control scoped lifetimes.
- [Disposal](../lifetime/disposal) for how instances are torn down.
