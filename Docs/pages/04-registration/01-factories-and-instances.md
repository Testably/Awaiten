---
description: Build a service with a factory method or register an object you already have as an instance.
---

# Factories and instances

Most services are built by their constructor, and Awaiten calls it for you. Sometimes that is not enough. You need a bit of logic to build the service, or you already have the object in hand. That is what factories and instances are for.

## Factory

Point a registration at a method with `Factory = nameof(...)`. The method builds the service. Its parameters resolve from the graph, just like a constructor. The result is cached according to the lifetime.

```csharp
[Container]
[Singleton<Settings>]
[Singleton<IBrewer>(Factory = nameof(MakeBrewer))]
public static partial class CoffeeShop
{
    private static IBrewer MakeBrewer(Settings settings)
        => settings.PreferFilter ? new FilterBrewer() : new EspressoBrewer();
}
```

The method is `static`, like the container class that declares it. A factory that returns `Task<T>` makes the service async. See [Async initialization](../03-core-concepts/03-async-initialization.md).

:::note
A `Factory` cannot name an overloaded method ([AWT112](../09-diagnostics.md#awt112)), and a registration cannot set both `Factory` and `Instance` ([AWT110](../09-diagnostics.md#awt110)).
:::

## Instance

Hand Awaiten an object you already built with `Instance = nameof(...)`. Awaiten uses it as-is. It never constructs it and never disposes it, because the container does not own it.

```csharp
[Container]
[Singleton<MenuBoard>(Instance = nameof(Menu))]
public static partial class CoffeeShop
{
    private static MenuBoard Menu { get; } = MenuBoard.LoadFromDisk();
}
```

Use this for configuration objects or anything with a lifetime you manage yourself.

## Several producers of one service

A `Factory` or `Instance` registration that names only the service, like `[Singleton<IBrewer>(Factory = nameof(MakeBrewer))]`, is identified by its member. Two such registrations of one service with different members are two implementations, just like two classes: the first registration (or the container's over a module's) wins single resolution, and both are members of the collection. Registrations that name the same member share one instance. When another registration names the type as its implementation, as in `[Singleton<Grinder>]`, every registration of it is that one implementation and must produce it the same way ([AWT111](../09-diagnostics.md#awt111)).

An open generic `Factory` that names only its service, like `[Transient(typeof(IRepository<>), Factory = nameof(CreateRepository))]`, is identified by its member for every closing (see [Generic factories](./05-open-generics.md#generic-factories)). Two of them with different members both join `IEnumerable<IRepository<Order>>`, and the first (or the container's over a module's) wins `IRepository<Order>`. Open registrations that close into one implementation, such as the same factory or the same implementation type registered twice, share one instance and must agree on its lifetime and lifecycle directives, like closed registrations of it ([AWT107](../09-diagnostics.md#awt107), [AWT166](../09-diagnostics.md#awt166)).

## Disposal follows the real type

A factory can return an interface that hides a disposable concrete type. Awaiten still disposes it, because it tracks the actual object it got back, and it disposes it exactly once with its owner.

An `Instance` member is the opposite. You own it, so Awaiten leaves its disposal to you.

To keep a factory-built service but opt out of that disposal, set `SuppressDisposal = true`. Awaiten builds it and never disposes it, leaving teardown to you or to an `OnRelease` hook, which is how you [pool a rented object](../03-core-concepts/04-disposal.md#suppressing-disposal).

## Where to go next

- [Context-aware factories](../05-resolution/07-context-aware-factories.md) to build a service based on who asked for it.
- [Lifecycle hooks](../06-lifetime/03-lifecycle-hooks.md) to run code when an instance is activated or released.
