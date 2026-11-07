---
description: Reuse a production container in tests and replace only the services a test needs to control.
---

# Extending containers

A test that exercises the real composition usually wants all of it except one or two volatile services: the payment gateway that would charge a card, the clock that would make the result depend on the time of day. Copying the production container into the test project would work until the two drift apart. `Extends` lets one container start from another container's composition and replace only what differs.

## Extend a production container

Set `Extends` on the `[Container]` of the test project and register the doubles on it.

```csharp
[Container]
[Singleton<StripeGateway, IPaymentGateway>]
[Singleton<RealTimeSystem, ITimeSystem>]
[Singleton<Barista>]
public static partial class CoffeeShop;

public sealed class FakeGateway : IPaymentGateway
{
    public Receipt Charge(decimal amount) => new(amount);
}

[Container(Extends = typeof(CoffeeShop))]
[Singleton<FakeGateway, IPaymentGateway>]
public static partial class TestCoffeeShop;
```

`TestCoffeeShop` compiles as if every declaration of `CoffeeShop` were written on it: its registrations, `[Import]`s, `[Scan]`s, decorators, composites, `[ImportService]` declarations, `[InjectProperty]` entries, lifecycle hooks, [async initializer conventions](../03-core-concepts/03-async-initialization.md#which-conventions-apply), `Eager` singletons and options. Only the `FakeGateway` differs. The generated `TestCoffeeShop.Root` is a container of its own: nothing at run time links it to `CoffeeShop.Root`, and resolving from it is as fast as from any other container.

## Use it in a test

```csharp
[Test]
public async Task Barista_ChargesThePrice()
{
    using TestCoffeeShop.Root shop = new();

    Receipt receipt = shop.Resolve<Barista>().Serve(3.50m);

    await That(receipt.Amount).IsEqualTo(3.50m);
}
```

The `Barista` comes from the production composition, and its `IPaymentGateway` is the `FakeGateway`, so the test never reaches Stripe.

A double can also stay private to the test: nest it in the extending container, or nest the container beside it in the test class. The generated resolvers hand it out as its service, so nothing outside the test sees the type.

```csharp
[Container(Extends = typeof(CoffeeShop))]
[Singleton<FakeGateway, IPaymentGateway>]
public static partial class TestCoffeeShop
{
    private sealed class FakeGateway : IPaymentGateway
    {
        public Receipt Charge(decimal amount) => new(amount);
    }
}
```

## A replacement replaces in full

A registration written directly on the extending container replaces every inherited registration of the same service and key, before anything else is decided. The replaced registration is gone: it is never constructed, it is missing from `IEnumerable<T>` and from keyed collections, and its `Eager` construction and lifecycle hooks go with it. Because it is gone, it does not collide with the replacement either: a replacement with another lifetime or another `Factory` is not a conflict.

- **Keys.** A keyed registration replaces only the inherited registration with the same key. A `WhenInjectedInto` registration has a key of its own per consumer, so a plain registration does not replace it, and a `WhenInjectedInto` registration replaces only the one for the same consumer.
- **Collections.** A registration of `T` replaces every inherited `T`. The extending container cannot add a member to an inherited collection without replacing the others; register every member it should hold.
- **Open generics.** A closed registration replaces its closing of an inherited open generic, and the other closings still come from the base. An open registration replaces an inherited open registration of the same generic.
- **Fallback.** A registration with `Fallback` only fills a gap, as it does in a module, so it replaces nothing.
- **Decorators.** The inherited decorators wrap the replacement. A `[Decorate]` on the extending container wraps the inherited decorators, whatever their `Order`, so a test wraps production. An inherited decorator cannot be removed.
- **Shared instances.** When the base registers one implementation under several services, replacing one of them leaves the others on the production implementation, which then no longer shares an instance with the replacement.

```csharp
[Container]
[Singleton<PrinterChannel, IReceiptChannel>]
[Singleton<EmailChannel, IReceiptChannel>]
public static partial class Shop;

[Container(Extends = typeof(Shop))]
[Singleton<SmsChannel, IReceiptChannel>]   // IEnumerable<IReceiptChannel> holds only SmsChannel
public static partial class TestShop;
```

The whole graph is validated again. A replacement that breaks a dependency is reported like any other mistake: replacing a singleton's dependency with a scoped one is a [captive dependency](../09-diagnostics.md#awt105). A problem that the base already has is reported only on the base.

## Per-test doubles from the host

To hand each test its own double, declare the service `[ImportService<T>]` on the extending container. That replaces the inherited registration too, and the test supplies the instance through the external resolver. In the other direction, a registration on the extending container replaces an inherited `[ImportService<T>]`, so a test can register what production draws from its host.

```csharp
using System.Diagnostics.CodeAnalysis;

[Container(Extends = typeof(CoffeeShop))]
[ImportService<IPaymentGateway>]
public static partial class HostedTestCoffeeShop;

public sealed class GatewayResolver(IPaymentGateway gateway) : IExternalResolver
{
    public bool TryResolve(Type serviceType, object? key, [NotNullWhen(true)] out object? instance)
    {
        instance = serviceType == typeof(IPaymentGateway) ? gateway : null;
        return instance is not null;
    }
}

using HostedTestCoffeeShop.Root shop = new(new GatewayResolver(new FakeGateway()));
```

See [external services](../07-hosting/02-external-services.mdx) for the resolver and for the MS.DI bridge, which supplies it automatically.

## Extends compared with Import and Fallback

`[Import]` and `Extends` both pull in registrations, but they settle a collision differently:

| | `[Import]` of a `[Module]` | `Extends` of a `[Container]` |
|---|---|---|
| A registration of the container itself | wins single resolution; a strong module registration stays in `IEnumerable<T>` | replaces the inherited registration, collections included |
| Who decides that a registration can be replaced | the module author, per registration, with `Fallback` | the extending container, for any inherited registration |
| What else is inherited | the module's registrations, decorators, composites and imported services | the whole composition, including scans, imports, `[InjectProperty]` and options |
| How many | several modules | one base |

A module `Fallback` registration is dropped in full when the container replaces it, which is the behavior `Extends` gives every inherited registration. The difference is who chooses: `Fallback` makes a registration overridable for every importer, while `Extends` lets one extending container replace what it needs and keeps the production container strict.

## Options

The extending container validates its graph with the options of its base: `AsyncSafety` and `LifetimeSafety` are inherited. Setting either on a container with `Extends` is an error ([AWT225](../09-diagnostics.md#awt225)), so a test graph is checked exactly like the production graph it reuses.

## Accessibility

The generated extending container calls the inherited factories, instance members, lifecycle hooks and constructors from its own code, so it must be able to see them. A private member of the base, or an internal one in another assembly, is reachable from the base's own generated code but not from the extending container. Make such a member public, or internal and grant the test assembly access:

```csharp no-compile
[assembly: InternalsVisibleTo("CoffeeShop.Tests")]
```

When the base's assembly grants `InternalsVisibleTo` to any assembly, the base generates an `internal` wrapper for each private `Factory` or `Instance` member and lifecycle hook its registrations name, and an extending container in another assembly calls the wrapper. So a private member works there as soon as the test assembly has access. A member whose signature names a type private to the base, and a generic factory, get no wrapper. An extending container in the base's own assembly doesn't use the wrappers: make the member internal there.

The same applies to the base's `[Scan]`: it sweeps the base's assembly and matches what the base sees, and a match the extending container cannot access is an error rather than a silently missing service.

An implementation nested privately in the base, or in a class that contains the base, is built by the base. The extending container cannot construct such a type, so the base generates an `internal` factory for it, and the extending container calls that factory. Everything else the registration declares still applies: the lifetime, `Eager`, keys, `WhenInjectedInto`, `Fallback`, collections, lifecycle hooks, async initialization and disposal. A private match of the base's scan is built the same way, so the test graph holds the same services as the production graph.

```csharp
[Container]
[Singleton<BurrGrinder, IGrinder>]
[Scan<IPlugin>(As = ScanAs.Marker)]
public static partial class Roastery
{
    private sealed class BurrGrinder : IGrinder;

    private sealed class AuditPlugin : IPlugin;
}

[Container(Extends = typeof(Roastery))]
public static partial class TestRoastery;   // resolves IGrinder and IEnumerable<IPlugin> like Roastery
```

A container only generates these factories when it has private implementations, and an extending container never does. The factory sits in an `internal` nested class named `Internals`, so an extending container in another assembly needs `InternalsVisibleTo`, like for any other internal member of the base. A registration the extending container replaces never calls the factory.

The factory mirrors the constructor the base uses. It copies an `[Arg]` or `[FromKey]` of a constructor parameter onto its own parameter, and takes each injected property as a parameter with the property's name. It still can't build every private type. A type with an `Optional` or `Deferred` injected property, a hook parameter marked `[Arg]`, `[FromKey]` or `[Inject]`, or a parameter type private to the base is not built for the extending container. A private decorator or composite of the base is built through a factory of its own, which takes the inner service or the collection. An open generic registration private to the base is not built for the extending container. Make such a type `internal`. A type nested privately in an imported module, or in a class that contains the module, is fine, because every container builds it through the module's generated factory.

Anything inherited that the extending container cannot reach or build is reported on its `Extends` argument ([AWT226](../09-diagnostics.md#awt226)), except a match of the base's scan: the extending container skips it with a warning ([AWT193](../09-diagnostics.md#awt193)), so a scan never fails the build.

## One level deep

The base must be another non-generic `[Container]` ([AWT222](../09-diagnostics.md#awt222)), not the container itself ([AWT223](../09-diagnostics.md#awt223)), and it must not extend a container of its own ([AWT224](../09-diagnostics.md#awt224)). Extending stays one level deep, so every registration of an extending container comes either from its own declaration or from its one base.

## When not to reach for this

`Extends` is meant for tests and similar variants of one composition, such as a container for a local demo that swaps the payment gateway. It is not a way to build an inheritance hierarchy of production containers: shared wiring between several production containers belongs in [modules](./07-modules.md), which each container imports explicitly.

Keep unit tests free of containers altogether. A unit test constructs the class under test with its fakes directly; `Extends` is for the composition, integration and host-level tests that want the real graph. A test container that replaces half of its base is a hint that the production container composes too much in one place.

## Where to go next

- [Modules](./07-modules.md) to share registrations between containers.
- [External services](../07-hosting/02-external-services.mdx) for services the host supplies.
- [Design principles](../08-design-principles.md) for the composition root a test container reuses.
