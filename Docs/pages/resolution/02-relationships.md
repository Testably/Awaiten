# Relationships

A dependency does not have to be the service itself. It can be a way to get the service later, or more than once. These wrapper shapes are called relationships. A barista does not hold a hundred cups. It holds a way to make a cup when an order comes in.

## Func: build on demand

Ask for `Func<T>` to get a service each time you call it. A transient gives you a fresh one per call. A singleton gives you the same one.

```csharp
public sealed class Barista(Func<Cup> cups)
{
    public Cup ServeNext() => cups();   // a fresh cup every order
}
```

## Lazy: build once, on first use

`Lazy<T>` defers construction until the first access, then memoizes it. Good for something expensive that might not be needed.

```csharp
public sealed class Register(Lazy<MenuBoard> menu)
{
    public string Today() => menu.Value.Special;   // built on first read
}
```

## Task: the async shape

`Task<T>` is how a synchronous service holds an async one. See [Async initialization](../async-initialization) for the full story.

```csharp
public sealed class Barista(Task<EspressoMachine> machine)
{
    public async Task<Cup> PullAsync() => (await machine).Pull();
}
```

## Relationships bind to their owner

A relationship resolves from the scope that built the consumer, not from wherever it is later called. A singleton that holds a `Func<Order>` resolves that `Order` from the root, always. It is never rebound to some child scope that happens to call it later. This keeps lifetimes predictable.

## Reaching shorter-lived services

Relationships are the clean way for a long-lived service to reach a shorter-lived one. A singleton cannot depend on a scoped service directly ([AWT105](../diagnostics#awt105)), but it can hold a `Func<T>` or `Lazy<T>` and pull it when needed.

*Note: in the strict default, a root-owned `Func<T>` over a disposable transient is withheld, because it would pile up undisposed instances on the root. Use [Owned](../lifetime/owned) instead, or switch to [Loose lifetime safety](../lifetime/lifetime-safety).*

## Where to go next

- [Runtime arguments](./runtime-arguments) for `Func<TArg, T>` with values you pass in.
- [Owned](../lifetime/owned) for disposable services you rent and return.
