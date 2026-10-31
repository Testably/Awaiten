---
description: Rent a disposable service with Owned<T> and dispose everything its resolution built when you are done.
---

# Owned

`Owned<T>` is the leak-free way to get a disposable service on demand and control when it goes away. You rent a brew session, use it, and return it. When you dispose the handle, everything that resolution built is disposed with it, while shared singletons live on.

## Rent a service

Resolve `Owned<T>`, use its `Value`, and dispose the handle when done.

```csharp
using (Owned<BrewSession> rented = shop.Resolve<Owned<BrewSession>>())
{
    rented.Value.Brew();
} // the brew session and everything it built is disposed here
```

## Rent many

Inject `Func<Owned<T>>` to rent repeatedly. Each handle owns its own throwaway scope, so disposing one does not affect another.

```csharp
public sealed class Counter(Func<Owned<BrewSession>> sessions)
{
    public void Serve()
    {
        using Owned<BrewSession> session = sessions();
        session.Value.Brew();
    }
}
```

## Rent with an argument

Combine it with a runtime argument. See [Runtime arguments](../05-resolution/06-runtime-arguments.md).

```csharp
public sealed class Counter(Func<string, Owned<Cup>> cups)
{
    public void Serve(string size)
    {
        using Owned<Cup> cup = cups(size);
        // ...
    }
}
```

## What disposal covers

Disposing the handle releases the dedicated scope that built the service, including its transitive disposables. It never disposes the container root, and it never disposes a shared singleton the service depended on.

## Async

For an async service, use `Func<Task<Owned<T>>>`. The handle awaits initialization before handing back the value, and `await using` drains it through `DisposeAsync`.

```csharp
public sealed class Plant(Func<Task<Owned<Valve>>> valves)
{
    public async Task RunAsync()
    {
        await using Owned<Valve> valve = await valves();
        // ...
    }
}
```

:::note
An `Owned<T>` handle cannot be requested through a `Lazy<Owned<T>>` ([AWT121](../09-diagnostics.md#awt121)), since that would hide who owns the disposal.
:::

:::warning[Only the container creates `Owned<T>`]
A handle built with `new` disposes whatever scope it is given, the root included, so constructing one in your code is the warning [AWT205](../09-diagnostics.md#awt205).
:::

## Where to go next

- [Lifetime safety](./02-lifetime-safety.md) for why disposable transients are withheld from the root.
- [Runtime arguments](../05-resolution/06-runtime-arguments.md) for building with values you pass in.
