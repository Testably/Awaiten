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

## Lifetime

A composite is transient by default. Set `Lifetime` to change it.

```csharp
[Composite<CompositeReceiptChannel, IReceiptChannel>(Lifetime = AwaitenLifetime.Singleton)]
```

With no members registered, the composite still resolves and fans out to an empty collection.

*Note: only one composite may front a given service ([AWT132](../diagnostics#awt132)), and the composite's collection parameter must be of the composed service itself ([AWT130](../diagnostics#awt130), [AWT133](../diagnostics#awt133)).*

## Where to go next

- [Decorators](./decorators) to wrap a single service instead of merging many.
- [Collections](../resolution/collections) for resolving every implementation directly.
