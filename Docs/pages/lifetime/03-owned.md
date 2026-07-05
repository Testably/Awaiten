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

Combine it with a runtime argument. See [Runtime arguments](../resolution/runtime-arguments).

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

*Note: an `Owned<T>` handle cannot be requested through a `Lazy<Owned<T>>` ([AWT121](../diagnostics#awt121)), since that would hide who owns the disposal.*

## Where to go next

- [Lifetime safety](./lifetime-safety) for why disposable transients are withheld from the root.
- [Runtime arguments](../resolution/runtime-arguments) for building with values you pass in.
