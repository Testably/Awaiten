# Complete example

This page assembles a full coffee shop from the pieces in the rest of the docs. It shows a realistic container: async equipment, per-order scopes, keyed milk, a decorated payment terminal, and a module. Read it top to bottom and you have seen how the features fit together.

## The services

```csharp
using Awaiten;

// Shared equipment. The machine warms up asynchronously.
public sealed class EspressoMachine : IAsyncInitializable
{
    public bool IsHot { get; private set; }
    public async Task InitializeAsync(CancellationToken ct)
    {
        await HeatBoilerAsync(ct);
        IsHot = true;
    }
    public Shot Pull() => new();
}

public sealed class Grinder;
public sealed class MenuBoard { public static MenuBoard Load() => new(); }

// One order at a time.
public sealed class Order;
public sealed class Tab(Order order);

// Made fresh, sized at request time.
public sealed class Cup
{
    public Cup(EspressoMachine machine, [Arg] string size) => Size = size;
    public string Size { get; }
}

// Keyed milk.
public interface IMilk;
public sealed class OatMilk : IMilk;
public sealed class WholeMilk : IMilk;

// Several syrups, resolved as a collection.
public interface ISyrup;
public sealed class VanillaSyrup : ISyrup;
public sealed class CaramelSyrup : ISyrup;

// Payment, wrapped with logging.
public interface IPaymentTerminal { Task ChargeAsync(decimal amount); }
public sealed class CardTerminal : IPaymentTerminal
{
    public Task ChargeAsync(decimal amount) => Task.CompletedTask;
}
public sealed class LoggingTerminal(IPaymentTerminal inner) : IPaymentTerminal
{
    public Task ChargeAsync(decimal amount) => inner.ChargeAsync(amount);
}
```

## The payment module

Group payment wiring so it can be reused and tested on its own.

```csharp
[Module]
[Singleton<CardTerminal, IPaymentTerminal>]
[Decorate<LoggingTerminal, IPaymentTerminal>]
public static class PaymentModule;
```

## The container

```csharp
[Container(SyncResolveAfterInit = true)]
[Import(typeof(PaymentModule))]

// equipment
[Singleton<EspressoMachine>]
[Singleton<Grinder>]
[Singleton<MenuBoard>(Instance = nameof(Menu))]

// per order
[Scoped<Order>]
[Scoped<Tab>]

// made to order
[Transient<Cup>]

// keyed milk
[Singleton<OatMilk, IMilk>(Key = "Oat")]
[Singleton<WholeMilk, IMilk>(Key = "Whole")]

// syrups as a set
[Singleton<VanillaSyrup, ISyrup>]
[Singleton<CaramelSyrup, ISyrup>]
public static partial class CoffeeShop
{
    private static MenuBoard Menu { get; } = MenuBoard.Load();
}
```

## Opening the shop

Create the root and warm the equipment once at startup.

```csharp
await using var shop = new CoffeeShop.Root();
await shop.InitializeAsync();     // the espresso machine heats its boiler
```

## Serving an order

Each order runs in its own scope, so its `Order` and `Tab` are isolated and disposed when the order is done.

```csharp
using (IAwaitenScope order = shop.CreateScope())
{
    Tab tab = order.Resolve<Tab>();
    Cup cup = order.Resolve<Func<string, Cup>>()("Large");

    var syrups = order.Resolve<IEnumerable<ISyrup>>();
    var milk = order.Resolve<IReadOnlyDictionary<string, IMilk>>()["Oat"];

    IPaymentTerminal terminal = order.Resolve<IPaymentTerminal>();  // LoggingTerminal(CardTerminal)
    await terminal.ChargeAsync(4.50m);
} // the order, its tab, and the cup are disposed here
```

## Closing up

Disposing the root disposes the singletons in reverse creation order. Because the root is `await using`, any async-disposable service is drained through `DisposeAsync`.

## What the compiler checked

None of this wiring is trusted at runtime. Before the app ran, Awaiten verified that every dependency is registered, that no singleton captures a scoped service, that the async machine is never reached synchronously, and that the decorated terminal has something to decorate. A mistake in any of those would have failed the build.

## Where to go next

- [Getting started](./getting-started) if you skipped ahead.
- [Diagnostics](./diagnostics) for every check the compiler runs.
- [MS.DI bridge](./msdi-bridge) to run this under ASP.NET Core.
