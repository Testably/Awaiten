# Decorators

A decorator wraps a service in another that shares its interface. It is how you add logging, retries, or caching without touching the real implementation. The consumer asks for the interface and gets the wrapped version. It cannot tell the difference, and it cannot bypass the wrapper.

## Wrap a service

Register the real service, then add `[Decorate<TDecorator, TService>]`. The decorator takes the inner service as a constructor parameter.

```csharp
public sealed class LoggingTerminal(IPaymentTerminal inner) : IPaymentTerminal
{
    public Task ChargeAsync(decimal amount) { /* log, then */ return inner.ChargeAsync(amount); }
}

[Container]
[Singleton<CardTerminal, IPaymentTerminal>]
[Decorate<LoggingTerminal, IPaymentTerminal>]
public static partial class CoffeeShop;

// Resolve<IPaymentTerminal>() returns LoggingTerminal(CardTerminal)
```

## Chain several

Stack decorators by declaring more than one. They chain in declaration order, and the last declared ends up outermost.

```csharp
[Singleton<CardTerminal, IPaymentTerminal>]
[Decorate<RetryingTerminal, IPaymentTerminal>]
[Decorate<LoggingTerminal, IPaymentTerminal>]
// result: LoggingTerminal(RetryingTerminal(CardTerminal))
```

Need a different order? Set `Order`. A higher `Order` sits further out.

```csharp
[Decorate<LoggingTerminal, IPaymentTerminal>(Order = 1)]
[Decorate<RetryingTerminal, IPaymentTerminal>]
```

## Collections are decorated too

If a service has several implementations, each one is wrapped. A consumer that asks for `IEnumerable<IPaymentTerminal>` gets a collection of decorated terminals. There is no way to reach an undecorated instance.

## Lifetime and disposal

A decorator inherits the lifetime of what it decorates. Wrap a singleton and the decorator is a singleton. On teardown the chain is disposed outermost first, so the decorator is disposed before its inner service.

## Async inner services

If the inner service is async-initialized, the whole chain is async-tainted. Resolve it with `ResolveAsync`. A synchronous resolve is a build error ([AWT119](../diagnostics#awt119)). See [Async initialization](../async-initialization).

*Note: decorating a service that is not registered is a build error ([AWT123](../diagnostics#awt123)), and a decorator needs exactly one constructor parameter of the decorated type ([AWT124](../diagnostics#awt124)).*

## Where to go next

- [Composites](./composites) to expose many implementations as one.
- [Collections](../resolution/collections) for resolving every implementation.
