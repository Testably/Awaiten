# Composites

A composite is one service that stands in for many. The consumer asks for a single service, and behind it the composite fans the call out to every other implementation. It is the classic way to treat "notify the customer on all channels" as one call.

## Compose a service

Register the real implementations, then add `[Composite<TComposite, TService>]`. The composite takes a collection of the service in its constructor.

```csharp
public sealed class CompositeReceiptChannel(IEnumerable<IReceiptChannel> channels) : IReceiptChannel
{
    public Task SendAsync(Receipt receipt)
        => Task.WhenAll(channels.Select(c => c.SendAsync(receipt)));
}

[Container]
[Transient<PrinterChannel, IReceiptChannel>]
[Transient<EmailChannel, IReceiptChannel>]
[Transient<SmsChannel, IReceiptChannel>]
[Composite<CompositeReceiptChannel, IReceiptChannel>]
public static partial class CoffeeShop;
```

## Asking for the service gives you the composite

```csharp
IReceiptChannel channel = shop.Resolve<IReceiptChannel>();   // the composite
await channel.SendAsync(receipt);                            // printer, email, and SMS
```

The composite never contains itself. Its own collection parameter receives only the other members. A consumer that asks for `IReceiptChannel[]` still gets the bare channels, so you can reach them directly if you need to.

## Compose every closing of an open generic

To front every closing of an open generic service with a matching composite, use the `typeof` form with unbound generics (the generic `[Composite<,>]` form only takes closed types):

```csharp
public sealed class CompositeHandler<T>(IEnumerable<IHandler<T>> inner) : IHandler<T>
{
    public Task HandleAsync(T request) => Task.WhenAll(inner.Select(h => h.HandleAsync(request)));
}

[Container]
[Transient(typeof(EmailHandler<>), typeof(IHandler<>))]
[Transient(typeof(SmsHandler<>), typeof(IHandler<>))]
[Composite(typeof(CompositeHandler<>), typeof(IHandler<>))]
public static partial class CoffeeShop;

// Resolve<IHandler<Receipt>>() returns CompositeHandler<Receipt> fanning out to the Receipt handlers
```

Awaiten synthesizes a closed composite for every closing present in the graph. Each closing then follows the closed rules above: one façade per closing, excluded from its own fan-out. The composite's arity must match the service's, and it must expose the service with its type parameters in declaration order (`CompositeHandler<T> : IHandler<T>`).

## Lifetime

A composite is transient by default. Set `Lifetime` to change it.

```csharp
[Composite<CompositeReceiptChannel, IReceiptChannel>(Lifetime = AwaitenLifetime.Singleton)]
```

With no members registered, the composite still resolves and fans out to an empty collection.

*Note: only one composite may front a given service ([AWT132](../diagnostics#awt132)), and the composite's collection parameter must be of the composed service itself ([AWT130](../diagnostics#awt130), [AWT133](../diagnostics#awt133)). The open generic `typeof` form additionally requires an unbound generic ([AWT127](../diagnostics#awt127)) of matching arity ([AWT125](../diagnostics#awt125)), and skips a closing whose type arguments violate the composite's constraints ([AWT126](../diagnostics#awt126)).*

## Where to go next

- [Decorators](./decorators) to wrap a single service instead of merging many.
- [Collections](../resolution/collections) for resolving every implementation directly.
