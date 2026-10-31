---
description: Every AWT diagnostic that Awaiten reports at compile time, with an example that triggers it.
toc_max_heading_level: 3
---

# Diagnostics

Awaiten checks your wiring while it builds. A mistake that other containers would surface at startup, or on the first resolve in production, is a compile error here. The barista never reaches for a cup that is not there.

Every diagnostic has an `AWT###` id. Errors stop the build. Warnings point at something that is legal but probably not what you meant. This page is the full catalog, one entry per id with a concrete example that triggers it. The feature pages link here for the codes they mention.

The examples reuse a few coffee-shop types (`Grinder`, `EspressoMachine`, `Order`, `Cup`, `IMilk`, `IBrewer`, and so on). Only the detail that trips the diagnostic is spelled out in each snippet. Each entry ends with a **Fix:** that names the change that resolves it.

## Graph correctness

These check that the object graph can actually be built.

### AWT101

:::danger[Error]
A required dependency has no registration.
:::

```csharp expect=AWT101
public sealed class Cup(Grinder grinder);

[Container]
[Transient<Cup>]        // Grinder is never registered
public static partial class CoffeeShop;
```

**Fix:** Register the missing dependency on the container (here `[Singleton<Grinder>]`) or in an imported module, or declare it `[ImportService<Grinder>]` when the host provides it.

### AWT102

:::danger[Error]
A dependency cycle exists in the object graph.
:::

```csharp expect=AWT102
public sealed class Till(Printer printer);
public sealed class Printer(Till till);   // Till needs Printer needs Till

[Container]
[Transient<Till>]
[Transient<Printer>]
public static partial class CoffeeShop;
```

**Fix:** Defer one edge of the cycle with `Func<T>` (or `Func<Task<T>>`), which resolves on demand instead of during construction, or with an [`[Inject(Deferred = true)]`](./05-resolution/05-property-injection.md#break-a-cycle-with-deferred) property. A bare `Owned<T>` or `Task<T>` resolves at construction and does not break it. Better still, split the responsibility so the two types no longer need each other.

```csharp
public sealed class Till(Printer printer);
public sealed class Printer(Func<Till> till);   // resolves the till on demand, so construction ends

[Container]
[Transient<Till>]
[Transient<Printer>]
public static partial class CoffeeShop;
```

### AWT103

:::danger[Error]
An implementation type is abstract or an interface, and no `Factory` or `Instance` member produces it.
:::

```csharp expect=AWT103
public interface IBrewer;

[Container]
[Singleton<IBrewer>]    // IBrewer is an interface, not something to construct
public static partial class CoffeeShop;
```

**Fix:** Register a concrete implementation for the service, such as `[Singleton<EspressoBrewer, IBrewer>]`, or produce the instance with a `Factory` or `Instance` member as shown next.

A `Factory` (or, on a singleton, `Instance`) member lifts the requirement: the container constructs nothing then, so the type argument only names the service the produced instance is resolved as. That is the shape for a library factory whose concrete type cannot be named: an internal implementation behind a public interface, such as the logger a `SerilogLoggerFactory` creates.

```csharp
[Container]
[Singleton<IBrewer>(Factory = nameof(CreateBrewer))]    // the factory produces it; IBrewer names the service
public static partial class CoffeeShop
{
    private static IBrewer CreateBrewer() => BrewerLibrary.Create();   // returns an internal implementation
}
```

### AWT104

:::danger[Error]
An implementation type has no accessible constructor.
:::

```csharp expect=AWT104
public sealed class Grinder
{
    private Grinder() { }   // only a private constructor
}

[Container]
[Singleton<Grinder>]
public static partial class CoffeeShop;
```

**Fix:** Give the type a constructor the container can call (public, or internal within the container's assembly), or register it with a `Factory` method that obtains the instance another way, for example through a static creation method on the type.

### AWT105

:::danger[Error]
A singleton captures a shorter-lived scoped dependency.
:::

```csharp expect=AWT105
public sealed class EspressoMachine(Order order);   // singleton needs a scoped Order

[Container]
[Singleton<EspressoMachine>]
[Scoped<Order>]
public static partial class CoffeeShop;
```

**Fix:** Register the consumer with the shorter lifetime too (`[Scoped<EspressoMachine>]`), or let it take `Func<Order>`, which resolves the order on demand instead of capturing it.

### AWT107

:::danger[Error]
An implementation is registered with conflicting lifetimes.
:::

```csharp expect=AWT107
[Container]
[Singleton<Grinder>]
[Transient<Grinder>]    // same implementation, two lifetimes
public static partial class CoffeeShop;
```

**Fix:** Register the implementation with a single lifetime attribute and remove the other.

### AWT111

:::danger[Error]
An implementation is registered with conflicting production strategies.
:::

```csharp expect=AWT111
[Container]
[Singleton<Grinder>(Factory = nameof(MakeGrinder))]
[Singleton<Grinder>]    // one via factory, one via constructor
public static partial class CoffeeShop
{
    private static Grinder MakeGrinder() => new();
}
```

**Fix:** Produce the implementation one way only: keep either the `Factory` registration or the constructor registration, or name the same `Factory` on both.

### AWT116

:::danger[Error]
A `[Container]` class is not declared static.
:::

```csharp expect=AWT116
[Container]
public partial class CoffeeShop;   // must be static partial
```

**Fix:** Declare the class `static partial` and create the container through its generated root: `new CoffeeShop.Root()`.

### AWT117

:::danger[Error]
Two registrations share the same service type and key.
:::

```csharp expect=AWT117
[Container]
[Singleton<OatMilk, IMilk>(Key = "Oat")]
[Singleton<SoyMilk, IMilk>(Key = "Oat")]   // same service and key
public static partial class CoffeeShop;
```

**Fix:** Give each registration its own key (here `Key = "Soy"` for `SoyMilk`), or remove the duplicate.

### AWT148

:::warning[Warning]
Two overridable default registrations provide the same service ambiguously.
:::

```csharp expect=AWT148
[Module]
[Singleton<RealTimeSystem, ITimeSystem>(Fallback = Fallback.Warn)]
public static class TimeModuleA;

[Module]
[Singleton<MockTimeSystem, ITimeSystem>(Fallback = Fallback.Warn)]
public static class TimeModuleB;

[Container]
[Import(typeof(TimeModuleA))]
[Import(typeof(TimeModuleB))]   // two defaults for ITimeSystem, neither wins
public static partial class CoffeeShop;
```

**Fix:** Decide the winner with a strong registration of the service on the container (one without `Fallback`), or set `Fallback = Fallback.Silent` on a default to accept that the first declared one wins.

## Async safety

These keep async-initialized services from being reached before they are ready.

### AWT106

:::warning[Warning]
A synchronous factory's body provably produces an `IAsyncInitializable` concrete type its declared return type hides.
:::

```csharp expect=AWT106
public sealed class AsyncBrewer : IBrewer, IAsyncInitializable
{
    public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;
}

[Container]
[Singleton<IBrewer>(Factory = nameof(MakeBrewer))]
public static partial class CoffeeShop
{
    // declared IBrewer, but the concrete type is async-initialized
    private static IBrewer MakeBrewer() => new AsyncBrewer();
}
```

**Fix:** Declare the concrete type as the factory's return type (here `AsyncBrewer`), so the container sees the initialization, or make the factory `async Task<IBrewer>` and initialize the instance inside it.

### AWT119

:::danger[Error]
A synchronous `Func`/`Lazy`/`Owned` relationship targets an async-initialized service.
:::

```csharp expect=AWT119
public sealed class EspressoMachine : IAsyncInitializable
{
    public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;
}
public sealed class Barista(Func<EspressoMachine> machine);   // sync Func over an async service

[Container]
[Singleton<EspressoMachine>]
[Singleton<Barista>]
public static partial class CoffeeShop;
```

**Fix:** Take a deferred async shape such as `Func<Task<EspressoMachine>>`, `Task<EspressoMachine>` or `Lazy<Task<EspressoMachine>>` and await it, which keeps the consumer synchronously resolvable. Alternatively resolve through `ResolveAsync`, or set `AsyncSafety.SyncAfterInit` on the `[Container]` and warm it with `InitializeAsync`.

```csharp
public sealed class Barista(Func<Task<EspressoMachine>> machine);   // awaited on demand

[Container]
[Singleton<EspressoMachine>]
[Singleton<Barista>]
public static partial class CoffeeShop;
```

### AWT120

:::danger[Error]
A synchronous `Func`/`Lazy`/`Owned` relationship reaches an async-tainted service transitively.
:::

```csharp expect=AWT120
public sealed class EspressoMachine : IAsyncInitializable
{
    public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;
}
public sealed class Grinder(EspressoMachine machine);   // async-tainted through the machine
public sealed class Barista(Func<Grinder> grinder);      // sync Func reaches it transitively

[Container]
[Singleton<EspressoMachine>]
[Transient<Grinder>]
[Singleton<Barista>]
public static partial class CoffeeShop;
```

**Fix:** As for [AWT119](#awt119): take `Func<Task<Grinder>>` (or another awaited shape) and await it, resolve through `ResolveAsync`, or set `AsyncSafety.SyncAfterInit` on the `[Container]`.

### AWT122

:::danger[Error]
A collection dependency has an async-tainted member but is materialized synchronously.
:::

```csharp expect=AWT122
public sealed class Latte : IDrink, IAsyncInitializable
{
    public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;
}
public sealed class Menu(IEnumerable<IDrink> drinks);   // sync collection with an async member

[Container]
[Transient<Latte, IDrink>]
[Singleton<Menu>]
public static partial class CoffeeShop;
```

**Fix:** Consume the collection through a shape that awaits its members, `IAsyncEnumerable<IDrink>` or `Task<IReadOnlyList<IDrink>>`, set `AsyncSafety.SyncAfterInit` on the `[Container]` and resolve after `InitializeAsync`, or keep the async member out of the collection.

### AWT156

:::warning[Warning]
A generated `Root`/`Scope` is disposed synchronously although its container owns a service that implements `IAsyncDisposable` but not `IDisposable`.
:::

```csharp expect=AWT156
public sealed class Boiler : IAsyncDisposable
{
    public ValueTask DisposeAsync() => default;
}

[Container]
[Singleton<Boiler>]
public static partial class CoffeeShop;

using (CoffeeShop.Root shop = new()) { }   // sync dispose cannot tear down Boiler; use await using
```

**Fix:** Dispose asynchronously: `await using (CoffeeShop.Root shop = new()) { }`, or `await shop.DisposeAsync()`.

### AWT161

:::danger[Error]
An eager singleton is async-initialized and cannot be constructed synchronously at container build time.
:::

```csharp expect=AWT161
public sealed class EspressoMachine : IAsyncInitializable
{
    public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;
}

[Container]
[Singleton<EspressoMachine>(Eager = true)]   // async service cannot be built eagerly at construction
public static partial class CoffeeShop;
```

**Fix:** Remove `Eager = true`: `InitializeAsync` on the root already warms every async singleton. If it must be built at construction, opt into `[Container(AsyncSafety = AsyncSafety.SyncAfterInit)]`.

## Factories and instances

### AWT108

:::danger[Error]
A `Factory` registration names a member that is not a usable factory method.
:::

```csharp expect=AWT108
[Container]
[Singleton<Grinder>(Factory = nameof(Count))]   // Count is a field, not a factory method
public static partial class CoffeeShop
{
    private static int Count = 3;
}
```

**Fix:** Point `Factory` at a method on the container (or on the declaring module for an imported registration) that returns the registered service type, here `private static Grinder MakeGrinder() => new();`.

### AWT109

:::danger[Error]
An `Instance` registration names a member that is not a usable instance member.
:::

```csharp expect=AWT109
[Container]
[Singleton<Grinder>(Instance = nameof(MakeGrinder))]   // MakeGrinder is a method, not an instance member
public static partial class CoffeeShop
{
    private static Grinder MakeGrinder() => new();
}
```

**Fix:** Point `Instance` at a field or property of the service type, such as `private static Grinder Shared { get; } = new();`, or switch to `Factory = nameof(MakeGrinder)` to keep the method.

### AWT110

:::danger[Error]
A registration sets both `Factory` and `Instance`.
:::

```csharp expect=AWT110
[Container]
[Singleton<Grinder>(Factory = nameof(MakeGrinder), Instance = nameof(Shared))]
public static partial class CoffeeShop
{
    private static Grinder MakeGrinder() => new();
    private static Grinder Shared { get; } = new();
}
```

**Fix:** Keep exactly one of the two: `Factory` to build the service, or `Instance` to hand over an object you own.

### AWT112

:::danger[Error]
A `Factory` registration names an overloaded method.
:::

```csharp expect=AWT112
[Container]
[Singleton<Grinder>(Factory = nameof(MakeGrinder))]   // MakeGrinder is overloaded
public static partial class CoffeeShop
{
    private static Grinder MakeGrinder() => new();
    private static Grinder MakeGrinder(int burrs) => new();
}
```

**Fix:** Give the factory method a unique name, for example `MakeGrinderWithBurrs` for the overload.

### AWT162

:::danger[Error]
A `[RequestingType]` factory parameter is not of type `System.Type`.
:::

```csharp expect=AWT162
[Container]
[Transient<ILogger>(Factory = nameof(CreateLogger))]
public static partial class CoffeeShop
{
    private static Logger CreateLogger([RequestingType] string consumer) => new(consumer);   // must be Type
}
```

**Fix:** Declare the parameter as `Type`: `[RequestingType] Type consumer`, and derive the name from it if you need one.

### AWT163

:::danger[Error]
A factory has both a `[RequestingType]` parameter and an `[Arg]` runtime-argument parameter.
:::

```csharp expect=AWT163
[Container]
[Transient<ILogger>(Factory = nameof(CreateLogger))]
public static partial class CoffeeShop
{
    private static Logger CreateLogger([RequestingType] Type consumer, [Arg] string name)
        => new(consumer, name);
}
```

**Fix:** Remove one of the two: drop `[Arg]` to keep the per-consumer factory, or drop `[RequestingType]` to make it a parameterized factory reached through `Func<string, ILogger>`.

### AWT186

:::danger[Error]
An `Owned<T>` relationship (or its `Func`/`Task` forms) targets a service produced by a requesting-type factory. Such a factory is called per consumer and decides its own disposal, so it has no owner scope to build the owned target into. Consume the service directly, or through `Func<T>` / `Lazy<T>`.
:::

```csharp expect=AWT186
public sealed class Barista(Owned<ILogger> logger);   // Owned<T> over a requesting-type factory

[Container]
[Transient<ILogger>(Factory = nameof(CreateLogger))]
[Transient<Barista>]
public static partial class CoffeeShop
{
    private static Logger CreateLogger([RequestingType] Type? consumer) => new(consumer?.Name ?? "<root>");
}
```

**Fix:** Take the service directly (`Barista(ILogger logger)`), or through `Func<ILogger>` or `Lazy<ILogger>`.

## Lifecycle hooks

### AWT164

:::danger[Error]
An `OnActivated`/`OnRelease` registration names a member that is not a usable lifecycle hook.
:::

```csharp expect=AWT164
[Container]
[Singleton<EspressoMachine>(OnActivated = nameof(Ready))]   // Ready is a field, not a hook method
public static partial class CoffeeShop
{
    private static bool Ready = false;
}
```

**Fix:** Point the hook at an accessible `static void` method whose first parameter accepts the implementation: `private static void Ready(EspressoMachine machine) { }`.

### AWT165

:::danger[Error]
An `OnActivated`/`OnRelease` lifecycle hook is set on a pre-built `Instance` registration, which the container does not own.
:::

```csharp expect=AWT165
[Container]
[Singleton<MenuBoard>(Instance = nameof(Menu), OnRelease = nameof(Archive))]
public static partial class CoffeeShop
{
    private static MenuBoard Menu { get; } = new();
    private static void Archive(object board) { }   // the container does not own an Instance
}
```

**Fix:** Remove the hook and run that code where you create the instance yourself, or register the type for construction (by constructor or `Factory`) instead of as an `Instance`.

### AWT166

:::danger[Error]
An implementation is registered with conflicting `OnActivated`/`OnRelease`/`Eager` directives that coalescing would silently drop.
:::

```csharp expect=AWT166
[Container]
[Singleton<EspressoMachine, IWarmable>(OnActivated = nameof(Calibrate))]
[Singleton<EspressoMachine, IMachine>(OnActivated = nameof(Preheat))]   // two hooks for one implementation
public static partial class CoffeeShop
{
    private static void Calibrate(object m) { }
    private static void Preheat(object m) { }
}
```

**Fix:** Declare one hook for the implementation: set it on one registration and leave it unset on the others (an unset directive merges with the winner's), or name the same method on all of them.

### AWT189

:::danger[Error]
A lifecycle hook parameter (after the instance) is marked `[Arg]`, but a hook resolves its parameters from the graph.
:::

```csharp expect=AWT189
[Container]
[Singleton<EspressoMachine>(OnActivated = nameof(Calibrate))]
public static partial class CoffeeShop
{
    private static void Calibrate(EspressoMachine machine, [Arg] int count) { }   // no Func<…> call site supplies an [Arg]
}
```

**Fix:** Drop `[Arg]` and let the parameter resolve from the graph, or move the value into a registered service the hook can take.

### AWT190

:::danger[Error]
A lifecycle hook (`OnActivated` / `OnRelease`) names an overloaded method, so there is no way to choose which one to call.
:::

A hook is looked up by simple name on its owner (the container, or a module whose `[Scan]` names it), so two accepting overloads leave the choice (and the graph dependencies the extra parameters resolve) order-dependent. Give the hook a unique name, exactly as a factory method must be unambiguous.

```csharp expect=AWT190
[Container]
[Singleton<EspressoMachine>(OnActivated = nameof(Calibrate))]
public static partial class CoffeeShop
{
    private static void Calibrate(EspressoMachine machine) { }
    private static void Calibrate(EspressoMachine machine, Settings settings) { }   // which one runs?
}
```

**Fix:** Rename one of the overloads, for example to `CalibrateWithSettings`, and point the hook at the one you mean.

### AWT191

:::danger[Error]
An `OnRelease` hook parameter is a `Func`/`Lazy` relationship, which would defer resolution past the owner's teardown.
:::

A release dependency is captured at construction, but a `Func<T>` or `Lazy<T>` captures only a resolver delegate. The hook runs while its owner is being disposed, so invoking the delegate there always throws. Take the dependency directly instead: it is resolved at construction and, released in reverse creation order, still alive when the hook uses it. An `OnActivated` hook may take `Func`/`Lazy` parameters freely, since it runs while the owner is alive.

```csharp expect=AWT191
[Container]
[Singleton<BufferPool>]
[Transient<Buffer>(OnRelease = nameof(ReturnToPool))]
public static partial class CoffeeShop
{
    private static void ReturnToPool(Buffer buffer, Func<BufferPool> pool) { }   // pool() would throw during teardown; take BufferPool directly
}
```

**Fix:** Take the dependency directly: `ReturnToPool(Buffer buffer, BufferPool pool)`.

## Runtime arguments

### AWT113

:::danger[Error]
A `Func<TArg...,T>` or `Func<TArg...,Task<T>>` relationship's runtime arguments do not match the service's `[Arg]` parameters.
:::

```csharp expect=AWT113
public sealed class Cup([Arg] string size);
public sealed class Counter(Func<int, Cup> cups);   // the [Arg] is string, not int

[Container]
[Transient<Cup>]
[Singleton<Counter>]
public static partial class CoffeeShop;
```

**Fix:** Make the `Func`'s argument types match the `[Arg]` parameters, in order: `Func<string, Cup>`.

### AWT114

:::danger[Error]
A service with `[Arg]` parameters is registered with a non-Transient lifetime.
:::

```csharp expect=AWT114
public sealed class Cup([Arg] string size);

[Container]
[Singleton<Cup>]    // [Arg] services must be transient
public static partial class CoffeeShop;
```

**Fix:** Register the service as `[Transient<Cup>]`; it is built fresh from its arguments on every request anyway.

### AWT115

:::danger[Error]
A service with `[Arg]` parameters is required as a plain, `Lazy<T>`, or `Task<T>` dependency instead of a `Func<TArg...,T>`.
:::

```csharp expect=AWT115
public sealed class Cup([Arg] string size);
public sealed class Barista(Cup cup);   // must ask for Func<string, Cup>

[Container]
[Transient<Cup>]
[Singleton<Barista>]
public static partial class CoffeeShop;
```

**Fix:** Depend on `Func<string, Cup>` and pass the size when you call it: `Barista(Func<string, Cup> cups)`.

### AWT137

:::note[Retired]
No longer reported. `[Arg]` is valid only on parameters, so the C# compiler rejects it on a property (CS0592). The id stays reserved and is never reused.
:::

```csharp expect=CS0592
public sealed class Barista
{
    [Inject, Arg] public string? Name { get; set; }   // CS0592: [Arg] is for constructor and factory parameters
}
```

**Fix:** Remove `[Arg]` from the property. A runtime argument goes on a constructor or factory parameter.

## Ownership and lifetime safety

### AWT118

:::danger[Error]
A root-owned instance holds a `Func` or `Func<…,Task<T>>` over a disposable build-on-demand service. Under strict lifetime safety (the default) this is a non-suppressible error; under `LifetimeSafety.Loose` it relaxes to a suppressible warning.
:::

```csharp expect=AWT118
public sealed class BrewSession : IDisposable { public void Dispose() { } }
public sealed class Counter(Func<BrewSession> sessions);   // each call strands a disposable on the root

[Container]                          // strict lifetime safety (the default)
[Transient<BrewSession>]
[Singleton<Counter>]
public static partial class CoffeeShop;
```

**Fix:** Hand out a disposal handle instead: take `Func<Owned<BrewSession>>` and dispose each handle when you are done with it (an async factory takes `Func<Task<Owned<T>>>`). A `Func<T>` held by a scoped consumer is bounded by its scope and is not reported.

```csharp
public sealed class Counter(Func<Owned<BrewSession>> sessions);   // each handle disposes its own session

[Container]
[Transient<BrewSession>]
[Singleton<Counter>]
public static partial class CoffeeShop;
```

### AWT121

:::danger[Error]
An `Owned<T>` disposal handle is requested through a `Lazy<Owned<T>>` or `Lazy<Task<Owned<T>>>` relationship.
:::

```csharp expect=AWT121
public sealed class BrewSession : IDisposable { public void Dispose() { } }
public sealed class Counter(Lazy<Owned<BrewSession>> sessions);   // Owned cannot hide behind Lazy

[Container]
[Transient<BrewSession>]
[Singleton<Counter>]
public static partial class CoffeeShop;
```

**Fix:** Request the handle directly as `Owned<BrewSession>`, or through `Func<Owned<BrewSession>>`, `Task<Owned<BrewSession>>` or `Func<Task<Owned<BrewSession>>>`.

### AWT192

:::danger[Error]
`SuppressDisposal` is set on a pre-built `Instance`, which the container does not own or dispose, so it has no effect. Remove it, or register the type for construction (by constructor or `Factory`) instead of as an `Instance`.
:::

```csharp expect=AWT192
public sealed class Boiler : IDisposable { public void Dispose() { } }

[Container]
[Singleton<Boiler>(Instance = nameof(Shared), SuppressDisposal = true)]   // an Instance is never disposed anyway
public static partial class CoffeeShop
{
    private static Boiler Shared { get; } = new();
}
```

**Fix:** Remove `SuppressDisposal`; a pre-built `Instance` is never disposed by the container. If the container should build the object but not dispose it, register it by constructor or `Factory` with `SuppressDisposal = true`.

### AWT214

:::danger[Error]
A container is projected into Microsoft.Extensions.DependencyInjection with `AddAwaitenContainer<TRoot>()` or `new AwaitenServiceProviderFactory<TRoot>()`, but it builds a possibly disposable service with `SuppressDisposal = true`. MS.DI disposes every disposable instance a projected registration hands out, so the suppression cannot hold, and the projection throws at startup. Remove `SuppressDisposal`, register a pre-built `Instance` you own, or resolve the service from the container directly through an [`AwaitenServiceProvider`](./07-hosting/01-msdi-bridge.md#or-let-awaiten-own-everything) instead of projecting the container. The analyzer sees a projection that names the container's `Root` in the container's own project; elsewhere the startup check still applies.
:::

```csharp expect=AWT214
using Awaiten.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

public sealed class Grinder : IDisposable { public void Dispose() { } }

[Container]
[Singleton<Grinder>(SuppressDisposal = true)]
public static partial class CoffeeShop;

new ServiceCollection().AddAwaitenContainer<CoffeeShop.Root>();   // MS.DI would dispose Grinder anyway
```

**Fix:** Remove `SuppressDisposal = true` from the projected container, register a pre-built `Instance` you own, or serve the service through an `AwaitenServiceProvider` instead of projecting the container.

## Decorators and composites

### AWT123

:::danger[Error]
A `[Decorate]` names a service with no registration to decorate. Without `Keys` or `AllKeys`, a decorator wraps only unkeyed registrations, so a service registered only under keys counts as not registered. Also raised when an open generic `[Decorate(typeof(D<>), typeof(IService<>))]` matches no closing of its service in the graph.
:::

```csharp expect=AWT123
public sealed class LoggingTerminal(IPaymentTerminal inner) : IPaymentTerminal;

[Container]
[Decorate<LoggingTerminal, IPaymentTerminal>]   // nothing registers IPaymentTerminal
public static partial class CoffeeShop;
```

**Fix:** Register the service before decorating it, for example `[Singleton<CardTerminal, IPaymentTerminal>]`. If it is registered only under keys, add `Keys` or `AllKeys = true` to the `[Decorate]`.

### AWT124

:::danger[Error]
A decorator has no single constructor parameter assignable to the decorated service type.
:::

```csharp expect=AWT124
public sealed class LoggingTerminal : IPaymentTerminal;   // no IPaymentTerminal inner parameter

[Container]
[Singleton<CardTerminal, IPaymentTerminal>]
[Decorate<LoggingTerminal, IPaymentTerminal>]
public static partial class CoffeeShop;
```

**Fix:** Give the decorator exactly one constructor parameter of the decorated service, which receives the inner instance: `LoggingTerminal(IPaymentTerminal inner)`.

### AWT130

:::danger[Error]
A `[Composite]` implementation has no collection parameter of the composed service to fan out to.
:::

```csharp expect=AWT130
public sealed class CompositeReceiptChannel : IReceiptChannel;   // no IEnumerable<IReceiptChannel> parameter

[Container]
[Transient<PrinterChannel, IReceiptChannel>]
[Composite<CompositeReceiptChannel, IReceiptChannel>]
public static partial class CoffeeShop;
```

**Fix:** Add a collection parameter of the composed service: `CompositeReceiptChannel(IEnumerable<IReceiptChannel> channels)`.

### AWT131

:::warning[Warning]
A `[Composite]` type is also registered as an ordinary member of the service it composes.
:::

```csharp expect=AWT131
public sealed class CompositeReceiptChannel(IEnumerable<IReceiptChannel> channels) : IReceiptChannel;

[Container]
[Transient<CompositeReceiptChannel, IReceiptChannel>]   // registered as a normal member too
[Composite<CompositeReceiptChannel, IReceiptChannel>]
public static partial class CoffeeShop;
```

**Fix:** Remove the ordinary registration and keep only the `[Composite]`; the composite is never a member of its own fan-out.

### AWT132

:::danger[Error]
More than one `[Composite]` names the same service.
:::

```csharp expect=AWT132
[Container]
[Composite<CompositeReceiptChannel, IReceiptChannel>]
[Composite<BackupReceiptChannel, IReceiptChannel>]   // two composites for one service
public static partial class CoffeeShop;
```

**Fix:** Keep one `[Composite]` per service and remove the others; only the first one is used.

### AWT133

:::danger[Error]
A `[Composite]`'s collection parameter is of a base type of the composed service, not the composed service itself.
:::

```csharp expect=AWT133
// IReceiptChannel : IChannel
public sealed class CompositeReceiptChannel(IEnumerable<IChannel> channels) : IReceiptChannel;   // IChannel is a base type

[Container]
[Transient<PrinterChannel, IReceiptChannel>]
[Composite<CompositeReceiptChannel, IReceiptChannel>]
public static partial class CoffeeShop;
```

**Fix:** Make the element type exactly the composed service: `IEnumerable<IReceiptChannel> channels`.

### AWT210

:::danger[Error]
A key in a `[Decorate]`'s `Keys` matches no registration of the decorated service, so that key has nothing to wrap. A `null` entry names the unkeyed registrations. For the open generic `typeof` form, no closing of the service is registered under the key.
:::

```csharp expect=AWT210
[Container]
[Singleton<CardTerminal, IPaymentTerminal>]
[Decorate<LoggingTerminal, IPaymentTerminal>(Keys = new object?[] { "backup" })]   // nothing is registered under "backup"
public static partial class CoffeeShop;
```

**Fix:** Register the decorated service under that key, or remove the key from `Keys`.

### AWT211

:::danger[Error]
A `[Decorate]` sets both `Keys` and `AllKeys`. `AllKeys` already selects every key, so set one or the other. The decorator is skipped.
:::

```csharp expect=AWT211
[Container]
[Singleton<CardTerminal, IPaymentTerminal>]
[Decorate<LoggingTerminal, IPaymentTerminal>(Keys = new object?[] { null }, AllKeys = true)]
public static partial class CoffeeShop;
```

**Fix:** Set `Keys` to decorate selected keys, or `AllKeys = true` to decorate every key, not both.

### AWT212

:::danger[Error]
A `[Composite]`'s `Key` matches no registration of the composed service, so the composite has nothing to front. For the open generic `typeof` form, no closing of the service is registered under the key.
:::

```csharp expect=AWT212
[Container]
[Transient<PrinterChannel, IReceiptChannel>]
[Composite<CompositeReceiptChannel, IReceiptChannel>(Key = "kitchen")]   // nothing is registered under "kitchen"
public static partial class CoffeeShop;
```

**Fix:** Register the members under that key (`[Transient<PrinterChannel, IReceiptChannel>(Key = "kitchen")]`), or change the composite's `Key`.

## Open generics

### AWT125

:::danger[Error]
An open generic registration's implementation and service have different arity. The open `[Decorate]`/`[Composite]` `typeof` forms are held to the same rule (decorator/composite arity must match the service).
:::

```csharp expect=AWT125
[Container]
[Transient(typeof(Repository<>), typeof(IRepository<,>))]   // one type parameter vs two
public static partial class CoffeeShop;
```

**Fix:** Pair an implementation and a service with the same number of type parameters: `[Transient(typeof(Repository<>), typeof(IRepository<>))]`.

### AWT126

:::danger[Error]
A required closed type violates the open generic implementation's type-parameter constraints, so the dependency cannot be satisfied. (For the open `[Decorate]`/`[Composite]` case, where the base service still resolves, see the warning [AWT171](#awt171).)
:::

```csharp expect=AWT126
public sealed class Repository<T> : IRepository<T> where T : class;
public sealed class Ledger(IRepository<int> repo);   // int is not a class

[Container]
[Transient(typeof(Repository<>), typeof(IRepository<>))]
[Singleton<Ledger>]
public static partial class CoffeeShop;
```

**Fix:** Depend on a closing that satisfies the constraints (here a reference type, such as `IRepository<Order>`), or relax the implementation's constraint.

### AWT171

:::warning[Warning]
The decorator/composite counterpart to [AWT126](#awt126): a closing of an open generic `[Decorate]`/`[Composite]` cannot be constructed because its type arguments violate the decorator's or composite's type-parameter constraints. A warning, not an error, because the base service still resolves. That one closing is left as-is (undecorated/unfronted), and the remaining closings are decorated/composed as usual.
:::

```csharp expect=AWT171
public interface IHandler<T> { }
public sealed class Handler<T> : IHandler<T> { }
public sealed class Logging<T>(IHandler<T> inner) : IHandler<T> where T : class;   // reference types only

public sealed class Counter(IHandler<int> handler);   // needs the closing IHandler<int>

[Container]
[Transient(typeof(Handler<>), typeof(IHandler<>))]
[Transient<Counter>]
[Decorate(typeof(Logging<>), typeof(IHandler<>))]   // IHandler<int> can't take Logging<int>: warned and skipped
public static partial class CoffeeShop;
```

**Fix:** Relax the decorator's or composite's constraint so it covers every closing (here drop `where T : class`). If the closing should stay undecorated, the warning says exactly that and can be left as is.

### AWT127

:::danger[Error]
The `typeof`-argument form of `[Decorate]` or `[Composite]` must receive unbound open generic types. A lifetime attribute's `typeof` form accepts a closed pair, but when one of its two types is an unbound generic, both must be.
:::

```csharp expect=AWT127
[Container]
[Transient(typeof(Repository<>), typeof(IRepository<Order>))]   // open implementation, closed service
public static partial class CoffeeShop;
```

**Fix:** Pass unbound generics on both sides, `typeof(Repository<>), typeof(IRepository<>)`, or register the closed pair with the generic form: `[Transient<Repository<Order>, IRepository<Order>>]`.

### AWT128

:::danger[Error]
An open generic implementation does not expose its service with type parameters in declaration order. The open `[Decorate]`/`[Composite]` `typeof` forms are held to the same rule.
:::

```csharp expect=AWT128
public sealed class Map<TKey, TValue> : IMap<TValue, TKey>;   // parameters swapped

[Container]
[Transient(typeof(Map<,>), typeof(IMap<,>))]
public static partial class CoffeeShop;
```

**Fix:** Expose the service with the implementation's type parameters in declaration order: `Map<TKey, TValue> : IMap<TKey, TValue>`.

### AWT129

:::danger[Error]
Open generic expansion nested too deep, indicating an unbounded generic recursion.
:::

```csharp expect=AWT129,AWT101
public sealed class Box<T>(Box<Box<T>> inner);   // Box<Order> needs Box<Box<Order>> needs ...
public sealed class Consumer(Box<Order> box);

[Container]
[Transient(typeof(Box<>))]
[Singleton<Consumer>]
public static partial class CoffeeShop;
```

**Fix:** Break the recursion: an implementation must not depend on an ever-larger closing of its own open registration, as `Box<T>` does with `Box<Box<T>>`.

### AWT206

:::danger[Error]
A closed `typeof` registration's implementation is not a class or interface assignable to its service. The generic form `[Singleton<Clock, IClock>]` rejects such a pair through its type constraints; the `typeof` form has none, so Awaiten reports it.
:::

```csharp expect=AWT206
public sealed class Clock;   // does not implement IClock

[Container]
[Singleton(typeof(Clock), typeof(IClock))]
public static partial class CoffeeShop;
```

**Fix:** Let the implementation implement the service (`public sealed class Clock : IClock;`), or use the generic form `[Singleton<Clock, IClock>]`, whose constraints check the pair.

### AWT207

:::danger[Error]
`Eager` or `Instance` is set on an open generic `typeof` registration. Both describe one concrete instance, but the closings of an open generic are whatever the graph asks for, so eagerly building "every closing" would start services because some other consumer happens to close the generic, and one pre-built instance cannot be every closing. Register the closings you want eager or pre-built with a closed type. The option is ignored.
:::

```csharp expect=AWT207
[Container]
[Singleton(typeof(Cache<>), typeof(ICache<>), Eager = true)]          // which closings?
[Singleton(typeof(Cache<Order>), typeof(ICache<Order>), Eager = true)]  // fine: one closed type
public static partial class CoffeeShop;
```

**Fix:** Remove `Eager` or `Instance` from the open generic registration and register each closing you want eager or pre-built with a closed type, as the second line does.

### AWT208

:::danger[Error]
The `Factory` of an open generic `typeof` registration is not a generic method with exactly the service's type parameters. Awaiten closes the factory per closing with the closed service's type arguments, in declaration order, so the method needs one type parameter for each of the service's. The registration is dropped.
:::

```csharp expect=AWT208
[Container]
[Transient(typeof(IRepository<>), Factory = nameof(Create))]
public static partial class CoffeeShop
{
    private static IRepository<Order> Create() => new Repository<Order>();   // needs Create<T>()
}
```

**Fix:** Make the factory a generic method with exactly the service's type parameters: `private static IRepository<T> Create<T>() => new Repository<T>();`.

### AWT209

:::danger[Error]
A closing of an open generic `typeof` registration violates the type-parameter constraints of its generic `Factory`, so the factory cannot be called for it. Like [AWT126](#awt126), the closing is skipped and the dependency that asked for it is unsatisfied.
:::

```csharp expect=AWT209
public sealed class Ledger(IRepository<int> numbers);   // int is not a class

[Container]
[Transient(typeof(IRepository<>), Factory = nameof(Create))]
[Singleton<Ledger>]
public static partial class CoffeeShop
{
    private static IRepository<T> Create<T>() where T : class => new Repository<T>();
}
```

**Fix:** Relax the factory's constraint so it covers the closing, or depend on a closing that satisfies it (here a reference type, such as `IRepository<Order>`).

## Property injection

### AWT136

:::danger[Error]
An `[Inject]` property has no set or init accessor the container can assign through.
:::

```csharp expect=AWT136
public sealed class Barista
{
    [Inject] public ITimeSystem? Time { get; }   // get-only, nothing to assign
}

[Container]
[Transient<Barista>]
[Singleton<RealTimeSystem, ITimeSystem>]
public static partial class CoffeeShop;
```

**Fix:** Give the property an accessible `set` or `init` accessor (`{ get; set; }`), or remove `[Inject]`.

### AWT144

:::danger[Error]
An `[Inject(Deferred = true)]` property is init-only or required rather than assignable after construction.
:::

```csharp expect=AWT144
public sealed class Node
{
    [Inject(Deferred = true)] public Node? Peer { get; init; }   // deferred needs a plain set
}

[Container]
[Singleton<Node>]
public static partial class CoffeeShop;
```

**Fix:** Give the property a plain `set` accessor and do not mark it `required`: `public Node? Peer { get; set; }`.

### AWT145

:::danger[Error]
A deferred property cycle consists entirely of transients and cannot terminate.
:::

```csharp expect=AWT145
public sealed class A { [Inject(Deferred = true)] public B? B { get; set; } }
public sealed class B { [Inject(Deferred = true)] public A? A { get; set; } }

[Container]
[Transient<A>]    // all transient: a fresh instance every step, so the cycle never closes
[Transient<B>]
public static partial class CoffeeShop;
```

**Fix:** Make at least one participant a singleton or scoped (`[Singleton<A>]`), so the cycle closes on a cached instance, or break the cycle.

### AWT146

:::danger[Error]
A deferred property cycle includes an async-initialized service and cannot terminate.
:::

```csharp expect=AWT146
public sealed class A : IAsyncInitializable
{
    [Inject(Deferred = true)] public B? B { get; set; }
    public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;
}
public sealed class B { [Inject(Deferred = true)] public A? A { get; set; } }

[Container]
[Singleton<A>]
[Singleton<B>]
public static partial class CoffeeShop;
```

**Fix:** Break the cycle, or make every participant synchronous: neither `IAsyncInitializable` nor dependent on an async service.

### AWT147

:::danger[Error]
A deferred property cycle still traverses a construction-time edge that duplicates a cached participant or recurses forever.
:::

```csharp expect=AWT147
public sealed class A(B b);                                       // construction-time edge into the cycle
public sealed class B { [Inject(Deferred = true)] public A? A { get; set; } }

[Container]
[Transient<A>]
[Transient<B>]
public static partial class CoffeeShop;
```

**Fix:** Turn the remaining constructor parameter into an `[Inject(Deferred = true)]` property too, and register one participant as a singleton or scoped, since a cycle of transients never terminates ([AWT145](#awt145)). Or remove one of the dependencies.

### AWT157

:::danger[Error]
An `[Inject(Optional = true)]` property is required and cannot be omitted from the object initializer.
:::

```csharp expect=AWT157
public sealed class Barista
{
    [Inject(Optional = true)] public required ITimeSystem Time { get; set; }   // required cannot be omitted
}

[Container]
[Transient<Barista>]
public static partial class CoffeeShop;
```

**Fix:** Drop `required`, or drop `Optional = true` so that a missing dependency is reported as [AWT101](#awt101) instead.

### AWT158

:::warning[Warning]
An `[Inject(Optional = true)]` property is init-only, so an unregistered dependency leaves it permanently at its default.
:::

```csharp expect=AWT158
public sealed class Barista
{
    [Inject(Optional = true)] public ITimeSystem? Time { get; init; }   // init-only stays default forever
}

[Container]
[Transient<Barista>]
public static partial class CoffeeShop;
```

**Fix:** Give the property a plain `set` accessor, so a value can still be assigned after construction when the dependency is not registered.

### AWT177

:::danger[Error]
An `[InjectProperty<T>]` names a member that is not a settable property on the implementation: an unknown name, a field or method, or a read-only property. For an open-generic target such as `[InjectProperty(typeof(OrderBook<>), ...)]` the generic definition is checked, once.
:::

```csharp expect=AWT177
public sealed class Barista
{
    public ITimeSystem? Time { get; set; }
}

[Container]
[Singleton<RealTimeSystem, ITimeSystem>]
[Singleton<Barista>]
[InjectProperty<Barista>("Tim")]   // no such property (typo); use nameof(Barista.Time)
public static partial class CoffeeShop;
```

**Fix:** Name the property with `nameof(Barista.Time)`, and make sure it is a property with a `set` or `init` accessor rather than a field, method or read-only member.

### AWT178

:::danger[Error]
An `[InjectProperty<T>]` targets an implementation produced by a `Factory` or `Instance` registration. Such an instance is built whole by its source, so there is no object initializer for the container to fill.
:::

```csharp expect=AWT178
public sealed class Barista
{
    public ITimeSystem? Time { get; set; }
}

[Container]
[Singleton<RealTimeSystem, ITimeSystem>]
[Singleton<Barista>(Factory = nameof(MakeBarista))]
[InjectProperty<Barista>(nameof(Barista.Time))]   // Barista is factory-produced, not container-constructed
public static partial class CoffeeShop
{
    private static Barista MakeBarista() => new();
}
```

**Fix:** Remove the `[InjectProperty]` entry and set the property in the factory, which can take the dependency as a parameter (`MakeBarista(ITimeSystem time) => new() { Time = time }`), or let the container construct the type.

### AWT179

:::warning[Warning]
Two `[InjectProperty]` entries name the same property of the same implementation, both closed or both open-generic. The property is filled once; the duplicate is ignored. A closed entry and an open-generic `typeof` entry that meet on one closing are reported only when the closed entry repeats the open entry's `Key`, `Optional` and `Deferred`: the closed entry wins on its closing either way, so one that changes an option is a deliberate [specialization of that closing](./05-resolution/05-property-injection.md#open-generics-with-the-typeof-form) and is not reported. This is an analyzer diagnostic, so a deliberate duplicate can be suppressed in source with `#pragma warning disable AWT179` or `[SuppressMessage]`.
:::

```csharp expect=AWT179
public sealed class Barista
{
    public ITimeSystem? Time { get; set; }
}

[Container]
[Singleton<RealTimeSystem, ITimeSystem>]
[Singleton<Barista>]
[InjectProperty<Barista>(nameof(Barista.Time))]
[InjectProperty<Barista>(nameof(Barista.Time))]   // filled once; the second entry is redundant
public static partial class CoffeeShop;
```

**Fix:** Remove the duplicate entry, or suppress the warning where the duplicate is deliberate.

### AWT180

:::warning[Warning]
An `[InjectProperty<T>]` names an implementation that has no container-constructed registration, so the entry is never applied: the type is unregistered, or it is an open generic no consumer closed.
:::

```csharp expect=AWT180
public sealed class Barista
{
    public ITimeSystem? Time { get; set; }
}

[Container]
[Singleton<RealTimeSystem, ITimeSystem>]
[InjectProperty<Barista>(nameof(Barista.Time))]   // Barista itself is never registered
public static partial class CoffeeShop;
```

**Fix:** Register the implementation as a constructed service (`[Singleton<Barista>]`), or remove the entry. For a generic definition, make sure a consumer requires one of its closings.

### AWT181

:::warning[Warning]
A property carries both `[Inject]` and a container-side `[InjectProperty<T>]` entry. The property is filled once, from `[Inject]`, so the entry's `Optional`/`Deferred`/`Key` are ignored.
:::

```csharp expect=AWT181
public sealed class Barista
{
    [Inject] public ITimeSystem? Time { get; set; }
}

[Container]
[Singleton<RealTimeSystem, ITimeSystem>]
[Singleton<Barista>]
[InjectProperty<Barista>(nameof(Barista.Time))]   // [Inject] already fills it: remove one
public static partial class CoffeeShop;
```

**Fix:** Remove one of the two: the `[Inject]` attribute on the property or the `[InjectProperty]` entry on the container.

## Scanning

### AWT138

:::warning[Warning]
A `[Scan]` matched no concrete type assignable to its marker.
:::

```csharp expect=AWT138
public interface IPlugin;   // no implementations exist

[Container]
[Scan<IPlugin>]
public static partial class CoffeeShop;
```

**Fix:** Check the marker for a typo and that its implementations live in the scanned assembly (use `InAssembliesOf` for another one), or remove the scan.

### AWT139

:::warning[Warning]
A `[Scan(As = ScanAs.Marker)]` matched a type with no assignable interface.
:::

```csharp expect=AWT139
public abstract class Report;
public sealed class QuarterlyReport : Report;   // a Report, but implements no interface

[Container]
[Scan(typeof(Report), As = ScanAs.Marker)]
public static partial class CoffeeShop;
```

**Fix:** Scan a marker interface, or add the `Self` flag to keep the self registration: `As = ScanAs.Self | ScanAs.Marker`.

### AWT140

:::warning[Warning]
A `[Scan(InAssembliesOf = …)]` named an assembly with no candidate types.
:::

```csharp expect=AWT140
[Container]
[Scan(typeof(IPlugin), InAssembliesOf = new[] { typeof(string) })]   // that assembly has no IPlugin types
public static partial class CoffeeShop;
```

**Fix:** Name a type from the assembly that holds the implementations, and check that its project is referenced and the marker is the right one.

### AWT141

:::warning[Warning]
A `[Scan(SkipUnconstructable = true)]` match the container cannot construct is skipped.
:::

```csharp expect=AWT141
public sealed class LegacyPlugin : IPlugin
{
    private LegacyPlugin() { }   // no usable constructor, so it is skipped
}

[Container]
[Scan(typeof(IPlugin), SkipUnconstructable = true)]
public static partial class CoffeeShop;
```

**Fix:** Make the match constructable (an accessible constructor, registered dependencies) so it registers, or exclude it from the scan to state that the skip is intended.

### AWT142

:::warning[Warning]
Scans register one implementation with conflicting lifetimes.
:::

```csharp expect=AWT142
public sealed class Widget : IPlugin, IHandler;   // matched by both scans

[Container]
[Scan(typeof(IPlugin), Lifetime = Lifetime.Singleton)]
[Scan(typeof(IHandler), Lifetime = Lifetime.Transient)]   // two lifetimes for Widget
public static partial class CoffeeShop;
```

**Fix:** Give the overlapping scans the same `Lifetime`, exclude the type from one of them, or register it explicitly, which the scans yield to.

### AWT143

:::danger[Error]
A `[Scan(InAssembliesOf = …)]` resolved to no assembly at all.
:::

```csharp expect=AWT143
[Container]
[Scan(typeof(IPlugin), InAssembliesOf = new Type[0])]   // no assemblies to scan
public static partial class CoffeeShop;
```

**Fix:** List one type from each assembly to scan, or remove `InAssembliesOf` to scan the container's own assembly.

### AWT172

:::warning[Warning]
A `[Scan]`'s name, namespace or exclude filters removed every marker-assignable match.
:::

```csharp expect=AWT172
public sealed class GrinderPlugin : IPlugin;   // assignable, but filtered out by the pattern below

[Container]
[Scan<IPlugin>(NamePatterns = ["*Handler"])]   // no IPlugin ends in "Handler", so nothing registers
public static partial class CoffeeShop;
```

**Fix:** Loosen the `NamePatterns`, `NamespacePatterns` or `Exclude` filters so they keep the types you mean, here `NamePatterns = ["*Plugin"]`.

### AWT173

:::warning[Warning]
A `[Scan]` exclusion (an `Exclude` type or a `!`-prefixed pattern) matched no candidate.
:::

```csharp expect=AWT173
public sealed class GrinderPlugin : IPlugin;

[Container]
[Scan<IPlugin>(Exclude = [typeof(RenamedPlugin)])]   // RenamedPlugin is no longer a candidate: stale
public static partial class CoffeeShop;
```

**Fix:** Remove the stale exclusion, or update it to the type's new name.

### AWT174

:::warning[Warning]
A `[Scan]` include pattern matches every candidate, so it does not narrow the scan.
:::

```csharp expect=AWT174
public sealed class GrinderPlugin : IPlugin;

[Container]
[Scan<IPlugin>(NamePatterns = ["*"])]   // "*" matches every name; drop it
public static partial class CoffeeShop;
```

**Fix:** Remove the pattern; it does not narrow anything.

### AWT182

:::warning[Warning]
A `[Scan(As = ScanAs.MatchingInterface)]` matched a type that implements no interface named `I` + its own name.
:::

```csharp expect=AWT182
public interface IEspresso;
public sealed class Espresso : IEspresso;
public sealed class Ristretto : IEspresso;   // no IRistretto, so it is not registered

[Container]
[Scan<IEspresso>(As = ScanAs.MatchingInterface)]
public static partial class CoffeeShop;
```

**Fix:** Implement the convention interface (`IRistretto`), or add the `Self` flag to keep the self registration: `As = ScanAs.Self | ScanAs.MatchingInterface`.

### AWT183

:::danger[Error]
A markerless `[Scan]` includes the `Marker` exposure, or declares no scoping filter.
:::

```csharp expect=AWT183
[Container]
[Scan(As = ScanAs.MatchingInterface)]   // markerless, but no NamePatterns/NamespacePatterns/InAssembliesOf
public static partial class CoffeeShop;
```

**Fix:** Give a markerless scan at least one `NamePatterns`, `NamespacePatterns` or `InAssembliesOf` filter, and leave `ScanAs.Marker` out of its `As`, since there is no marker.

### AWT184

:::warning[Warning]
A markerless `[Scan]` matched candidate types but registered none of them.
:::

```csharp expect=AWT184
[Container]
// Nothing under CoffeeShop.Legacy follows the I + name convention, so nothing registers.
[Scan(As = ScanAs.MatchingInterface, NamespacePatterns = ["CoffeeShop.Legacy.**"])]
public static partial class CoffeeShop;
```

**Fix:** Widen the filters until they match the types you mean, and with `ScanAs.MatchingInterface` check that the matches follow the `I` + name convention.

### AWT185

:::danger[Error]
A `[Scan]`'s `As` resolved to no `ScanAs` flag, so it would register nothing (usually `&` written for `|`).
:::

```csharp expect=AWT185
[Container]
[Scan<IDrink>(As = ScanAs.Self & ScanAs.Marker)]   // & is empty; use | to combine flags
public static partial class CoffeeShop;
```

**Fix:** Combine flags with `|` (`ScanAs.Self | ScanAs.Marker`), or omit `As` for the `Self` default.

### AWT187

:::warning[Warning]
A `[Scan(As = ScanAs.MatchingInterface)]` matched a type implementing several same-named convention interfaces, so it registers under each of them.
:::

```csharp expect=AWT187
namespace CoffeeShop.Old { public interface IMenu; }
namespace CoffeeShop.New { public interface IMenu; }

namespace CoffeeShop
{
    public interface IShopService;

    // No CoffeeShop.IMenu exists to win the own-namespace tiebreak, so Menu registers under both.
    public sealed class Menu : IShopService, Old.IMenu, New.IMenu;

    [Container]
    [Scan<IShopService>(As = ScanAs.MatchingInterface)]
    public static partial class Shop;
}
```

Reported only when several interfaces actually register (an inaccessible candidate is dropped, which [AWT188](#awt188) covers when the match registers nothing) and only for a marker scan: a markerless `[Scan]` registers the ambiguous match silently, like the other per-match scan warnings.

**Fix:** Keep a single convention interface, declare the one you want in the type's own namespace so it wins the tiebreak, or exclude the type from the scan.

### AWT188

:::warning[Warning]
A scan match's only exposure interface is inaccessible to the generated container, so the match is not registered.
:::

```csharp no-compile
// In a referenced assembly: the convention interface is internal.
internal interface IRoaster;
public sealed class Roaster : IRoaster, IEquipment;

// Registering Roaster under IRoaster would not compile in the container's assembly.
[Container]
[Scan<IEquipment>(As = ScanAs.MatchingInterface, InAssembliesOf = [typeof(IEquipment)])]
public static partial class CoffeeShop;
```

**Fix:** Make the interface accessible to the container's assembly (public, or `InternalsVisibleTo`), or add the `Self` flag to register the type under itself.

### AWT193

:::warning[Warning]
A `[Scan]` matched a type that is inaccessible to the generated container, so it is not registered.
:::

```csharp no-compile
// In a referenced assembly: the implementation itself is internal.
public interface IEquipment;
internal sealed class Roaster : IEquipment;

// Roaster is assignable to the marker, but the container cannot name it to construct it.
[Container]
[Scan<IEquipment>(InAssembliesOf = [typeof(IEquipment)])]
public static partial class CoffeeShop;
```

Where [AWT188](#awt188) is about an interface the match cannot be exposed under, this one is about the match itself, so no `ScanAs` flag rescues it: every registration has to name the implementation. Make the type public, grant the container's assembly `InternalsVisibleTo`, or exclude it with a `NamePatterns`/`NamespacePatterns` entry (the `Exclude` type list cannot name an inaccessible type). Reported only for a type the marker matched and the scan's filters kept, so an unrelated internal type in a scanned assembly stays silent, as does one you already excluded.

**Fix:** Make the type public, grant the container's assembly `InternalsVisibleTo`, or exclude it with a `NamePatterns` or `NamespacePatterns` entry.

### AWT198

:::danger[Error]
A generic lifecycle hook on an open-generic `[Scan]` marker could bind a match through more than one closed marker form, so its type arguments are ambiguous.
:::

```csharp expect=AWT198
public interface IView<TViewModel>;
public sealed class DualView : IView<Orders>, IView<Payments>;   // closes IView<> twice

[Container]
[Scan(typeof(IView<>), As = ScanAs.Marker, OnActivated = nameof(Wire))]
public static partial class CoffeeShop
{
    private static void Wire<TViewModel>(IView<TViewModel> view) { }   // TViewModel would be Orders or Payments?
}
```

A [generic scan hook](./04-registration/06-scanning.md#lifecycle-hooks) binds its type argument from a closed marker form, so it needs exactly one it can bind. `DualView`, which closes `IView<>` at both `Orders` and `Payments`, offers two; the same happens when two scans bind the same generic hook through differently-closed markers of one type. Register the type explicitly with the intended hook, or split the family so only one closed form binds it.

<details>
<summary>Which closed forms count</summary>

Only forms the hook could actually bind count: matching arity, type arguments the generated container can access, satisfied constraints, and a first parameter that accepts the match. If exactly one form remains after those checks the hook binds it, so a `where TViewModel : class` constraint can settle a family that closes the marker at one class and one struct, and a closing at another assembly's internal type yields to an accessible sibling. Reported only when the ambiguous generic method is the sole usable overload of the hook name: if another overload also accepts the match, the collision is between overloads rather than closings (settling the closings would still leave two usable overloads), so it is [AWT190](#awt190) instead. Only a *generic* hook is affected: a non-generic hook (its parameter typed as the marker or `object`) takes no type argument, so a match with several closings is fine for it, as is a match closing the marker several times *without* any hook (each closed form registers as its own collection member).

</details>

**Fix:** Register the type explicitly with the hook you intend, or split the family so only one closed marker form binds the hook.

### AWT199

:::warning[Warning]
Two `[Scan]` attributes match one implementation with conflicting lifecycle hooks; the first scan's hook is used.
:::

```csharp expect=AWT199
public sealed class Widget : IPlugin, IHandler;   // matched by both scans

[Container]
[Scan(typeof(IPlugin), OnActivated = nameof(PluginStarted))]
[Scan(typeof(IHandler), OnActivated = nameof(HandlerStarted))]   // two OnActivated hooks for Widget
public static partial class CoffeeShop
{
    private static void PluginStarted(object instance) { }
    private static void HandlerStarted(object instance) { }
}
```

Which method ran for `Widget` would depend on attribute order, so the contradiction is surfaced instead, mirroring [AWT142](#awt142) for lifetimes. Overlapping scans that agree are fine: two scans naming the *same* method merge, and so do scans hooking *different* slots (one scan's `OnActivated` combines with another's `OnRelease`). A hook name is owner-relative, though: a module `[Scan]` resolves its hooks against the module and a container `[Scan]` against the container, so a module scan and a container scan naming the same method name still conflict, because each means its own method. An explicit registration of the type is not reported either: a scan yields to it wholesale, hooks included, so the explicit registration's hooks (or its deliberate lack of them) replace the scan's.

**Fix:** Name the same hook method in both scans, or register the type explicitly with the hook it should get; a scan yields to an explicit registration, hooks included.

## Modules

### AWT149

:::danger[Error]
An `[Import]` names a type that is not marked `[Module]`.
:::

```csharp expect=AWT149
public static class PaymentHelpers;   // not marked [Module]

[Container]
[Import(typeof(PaymentHelpers))]
public static partial class CoffeeShop;
```

**Fix:** Import the intended `[Module]`, or mark the imported type `[Module]` and give it registrations.

### AWT150

:::danger[Error]
An imported module has its own `[Import]`, which is not followed.
:::

```csharp expect=AWT150
[Module]
[Import(typeof(BaseModule))]   // a nested import is not followed
[Singleton<CardTerminal, IPaymentTerminal>]
public static class PaymentModule;

[Container]
[Import(typeof(PaymentModule))]
public static partial class CoffeeShop;
```

**Fix:** Import the nested module on the container directly (`[Import(typeof(BaseModule))]`) and remove the `[Import]` from the module; imports are followed one level deep.

### AWT151

:::warning[Warning]
An imported module declares no registrations.
:::

```csharp expect=AWT151
[Module]
public static class EmptyModule;   // nothing to contribute

[Container]
[Import(typeof(EmptyModule))]
public static partial class CoffeeShop;
```

**Fix:** Add the registrations the module is meant to contribute, or remove the `[Import]`.

### AWT152

:::danger[Error]
An imported `[Module]` class is not declared static.
:::

```csharp expect=AWT152
[Module]
[Singleton<CardTerminal, IPaymentTerminal>]
public class PaymentModule;   // must be static

[Container]
[Import(typeof(PaymentModule))]
public static partial class CoffeeShop;
```

**Fix:** Declare the module `static`: `public static class PaymentModule;`.

### AWT153

:::danger[Error]
A module `Factory`/`Instance` member is not accessible from the generated container.
:::

```csharp expect=AWT153
[Module]
[Singleton<Grinder>(Factory = nameof(MakeGrinder))]
public static class EquipmentModule
{
    private static Grinder MakeGrinder() => new();   // private, so the container cannot call it
}

[Container]
[Import(typeof(EquipmentModule))]
public static partial class CoffeeShop;
```

**Fix:** Make the member `public`, or `internal` when the container is in the same assembly (or one the module's assembly grants `InternalsVisibleTo`).

### AWT154

:::danger[Error]
An imported module declares a `[Scan]`, but its assembly carries no generated expansion.
:::

```csharp no-compile
// In a referenced library that was compiled WITHOUT the Awaiten source generator:
[Module]
[Scan<IDrink>]   // never expanded, so it would contribute nothing
public static partial class MenuModule;

[Container]
[Import(typeof(MenuModule))]
public static partial class CoffeeShop;
```

:::note[Repurposed id]
This id previously rejected any `[Scan]` on a module.
:::

A `[Scan]` on a `[Module]` is [self-compiled in the module's own build](./04-registration/07-modules.md#self-compiled-scans), which stamps the module with a generated marker even when the scan matched nothing. A module whose metadata carries a `[Scan]` but no marker was compiled without the Awaiten generator, or with a version predating self-compiled scans, so its scan would silently contribute nothing. Rebuild the library with the Awaiten generator referenced. Reported at the consumer's `[Import]`. The self-compilation constraints themselves are reported as [AWT194](#awt194) to [AWT197](#awt197) and [AWT200](#awt200) to [AWT202](#awt202).

**Fix:** Rebuild the module's assembly with the Awaiten package referenced, so its generator expands the scan.

### AWT155

:::warning[Warning]
Two imported modules strongly register the same service with different implementations.
:::

```csharp expect=AWT155
[Module]
[Singleton<RealTimeSystem, ITimeSystem>]
public static class ModuleA;

[Module]
[Singleton<MockTimeSystem, ITimeSystem>]
public static class ModuleB;

[Container]
[Import(typeof(ModuleA))]
[Import(typeof(ModuleB))]   // both strongly register ITimeSystem
public static partial class CoffeeShop;
```

**Fix:** Override the service on the container with a registration of its own, or make one module's registration an overridable default with `Fallback = Fallback.Warn` or `Fallback = Fallback.Silent`.

### AWT194

:::danger[Error]
A `[Module]` that declares a `[Scan]` is not `partial` (or is nested in a type that is not), so its scan cannot be self-compiled.
:::

```csharp expect=AWT194
[Module]
[Scan<IPlugin>(As = ScanAs.MatchingInterface)]
public static class PluginModule;   // must be partial to receive the generated factories
```

A [self-compiled module scan](./04-registration/07-modules.md#self-compiled-scans) emits a factory method and a registration attribute into the module's partial, re-opening the whole nesting chain of a nested module. Add the `partial` modifier to the module class and every type containing it. Reported in the module's own build.

**Fix:** Add `partial` to the module and every type that contains it: `public static partial class PluginModule;`.

### AWT195

:::danger[Error]
A self-compiled module `[Scan]` match has a constructor parameter of a type inaccessible outside the module's assembly.
:::

```csharp expect=AWT195
internal sealed class Secret;
internal sealed class Roaster(Secret secret) : IRoaster;   // Secret is internal

[Module]
[Scan<IRoaster>(As = ScanAs.MatchingInterface)]
public static partial class PluginModule;
```

The generated factory is a `public` method whose parameters are resolved from the *consuming* container's graph, so each parameter type has to be nameable by the consumer. Widen the parameter type's accessibility, or exclude the match. (A v1 limitation: a self-compiled scan cannot construct a match through an inaccessible parameter.)

**Fix:** Make the parameter type public, or exclude the match from the scan.

### AWT196

:::warning[Warning]
A self-compiled module `[Scan]` match has no exposure interface accessible outside the module's assembly, so it is skipped.
:::

```csharp expect=AWT196
internal sealed class Roaster : IPlugin;

[Module]
[Scan<IPlugin>(As = ScanAs.Self)]   // Self exposes the internal type, which a consumer cannot name
public static partial class PluginModule;
```

A consumer resolves a self-compiled match only through an accessible interface. `ScanAs.Self` over an `internal` implementation exposes nothing nameable, so the match registers nothing and is skipped, mirroring the warning severity a container scan gives a match it cannot register ([AWT182](#awt182)/[AWT188](#awt188)/[AWT193](#awt193)). The skip holds even when the importing container lives in the module's own assembly, so a module scan registers the same matches wherever the module is imported from. Expose it through a public interface (`ScanAs.MatchingInterface` or `ScanAs.Marker`), or exclude the match.

**Fix:** Expose the match through a public interface with `ScanAs.MatchingInterface` or `ScanAs.Marker`, or exclude it from the scan.

### AWT197

:::danger[Error]
A self-compiled module `[Scan]` match would be exposed under more than one interface.
:::

```csharp expect=AWT197
internal sealed class Roaster : IPlugin, IRoaster;

[Module]
[Scan<IPlugin>(As = ScanAs.Marker | ScanAs.MatchingInterface)]   // IPlugin and IRoaster both apply
public static partial class PluginModule;
```

A self-compiled match is reached through a generated factory that returns a single accessible interface, so a shared instance across several interfaces cannot be expressed (unlike a container `[Scan]`, whose matches coalesce on the concrete type). The same applies across scans: two `[Scan]`s on one module that expose the same type under two different interfaces would emit two factories and silently split the instance, so that overlap is also rejected. Narrow the exposure to a single interface (typically `ScanAs.MatchingInterface`), or exclude the match. This is a v1 limitation.

**Fix:** Narrow `As` to a single interface, typically `As = ScanAs.MatchingInterface`, or exclude the match.

### AWT200

:::danger[Error]
A self-compiled module `[Scan]` match carries injection metadata the generated factory cannot mirror.
:::

```csharp expect=AWT200
internal sealed class Roaster : IRoaster
{
    public Roaster(IClock clock) { }

    [Inject]                              // keys, optionality and deferral live on this attribute
    public IGrinder Grinder { get; set; }
}

[Module]
[Scan<IRoaster>(As = ScanAs.MatchingInterface)]
public static partial class PluginModule;
```

The generated factory reduces a match to a plain parameter list resolved from the consuming container's graph. An `[Inject]` property, or a constructor parameter marked `[Inject]` or `[Arg]`, carries per-dependency semantics that plain parameters cannot express, so the consumer would silently construct the match differently than a container `[Scan]` would. Remove the attribute, exclude the match, or register the type through a hand-written module factory.

**Fix:** Remove the `[Inject]` or `[Arg]` attribute, exclude the match from the scan, or register the type through a hand-written module factory.

### AWT201

:::danger[Error]
A generic `[Module]` (or one nested in a generic type) declares a `[Scan]`.
:::

```csharp expect=AWT201
[Module]
[Scan<IPlugin>(As = ScanAs.MatchingInterface)]
public static partial class PluginModule<T>;   // no closed PluginModule<T> exists to [Import]
```

A consumer imports a module by `typeof`, so there is no single closed module type to import from a generic declaration, and the generated partial could not re-open it by its bare name. Move the `[Scan]` onto a non-generic module.

**Fix:** Move the `[Scan]` onto a non-generic module.

### AWT202

:::danger[Error]
A module `[Scan]` declares `InAssembliesOf`, but a self-compiled scan sweeps only the module's own assembly.
:::

```csharp expect=AWT202
[Module]
[Scan<IPlugin>(As = ScanAs.Marker, InAssembliesOf = new[] { typeof(OtherLibMarker) })]
public static partial class PluginModule;
```

A self-compiled scan exists to reach the module's own `internal` types. Sweeping another assembly from a module would see only that assembly's public types, which a container `[Scan]` with `InAssembliesOf` already covers, so the module form would silently do less than the container form. Remove `InAssembliesOf` to scan the module's own assembly, or move the `[Scan]` onto the container.

**Fix:** Remove `InAssembliesOf` to scan the module's own assembly, or move the `[Scan]` onto the container.

### AWT203

:::danger[Error]
A self-compiled module `[Scan]` hook has a parameter, after the instance, whose type is not accessible outside the module's assembly.
:::

```csharp expect=AWT203
internal sealed class Secret;
internal sealed class Roaster : IPlugin, IRoaster;

[Module]
[Scan<IPlugin>(As = ScanAs.MatchingInterface, OnActivated = nameof(Wire))]
public static partial class PluginModule
{
    internal static void Wire(Roaster roaster, Secret secret) { }   // Secret is internal to the module
}
```

The module emits a `public` wrapper for the hook, and that wrapper's parameters after the instance are resolved from the consuming container's graph, so each type has to be nameable by the consumer, the same rule a self-compiled factory parameter gets ([AWT195](#awt195)). The instance parameter itself is exempt: it is the accessible exposure interface, cast back to the internal implementation inside the module. Widen the parameter type's accessibility, resolve a different dependency, or drop the hook.

**Fix:** Make the parameter type public, take a different dependency, or drop the hook.

### AWT204

:::danger[Error]
A self-compiled module `[Scan]` hook has a parameter, after the instance, marked `[FromKey]` or `[Inject]`.
:::

```csharp expect=AWT204
internal sealed class Roaster : IPlugin, IRoaster;

[Module]
[Scan<IPlugin>(As = ScanAs.MatchingInterface, OnActivated = nameof(Wire))]
public static partial class PluginModule
{
    internal static void Wire(Roaster roaster, [FromKey("main")] IClock clock) { }
}
```

The hook's parameters after the instance mirror onto the generated `public` wrapper as a bare type-and-name signature, so the attribute's per-dependency semantics (a key, optionality, deferral) would be silently dropped: a cross-assembly consumer would resolve the plain type while a same-compilation container, binding the module's own hook directly, honored the attribute, so the same source would inject different instances depending on which side of the assembly boundary the consumer sits. This is the hook-parameter twin of the factory's [AWT200](#awt200), rejected rather than silently degraded. Remove the attribute, resolve the dependency plainly, or drop the hook.

**Fix:** Remove the `[FromKey]` or `[Inject]` attribute and resolve the dependency plainly, or drop the hook.

## Keyed collections

### AWT159

:::danger[Error]
A keyed dictionary (`IReadOnlyDictionary<TKey, TService>`) has a key type that is neither `string` nor an enum, or one whose keyed registrations do not all match it.
:::

```csharp expect=AWT159
public sealed class Router(IReadOnlyDictionary<int, IMilk> milks);   // the key must be string or an enum

[Container]
[Singleton<OatMilk, IMilk>(Key = "Oat")]
[Singleton<Router>]
public static partial class CoffeeShop;
```

**Fix:** Use a key type that every keyed registration of the service carries, a `string` or an enum: `IReadOnlyDictionary<string, IMilk>`.

### AWT160

:::danger[Error]
A `[FromKey]` is applied to a synthesized keyed collection (`IReadOnlyDictionary<string, TService>`), which resolves every key.
:::

```csharp expect=AWT160
public sealed class Router([FromKey("Oat")] IReadOnlyDictionary<string, IMilk> milks);   // the map already holds every key

[Container]
[Singleton<OatMilk, IMilk>(Key = "Oat")]
[Singleton<Router>]
public static partial class CoffeeShop;
```

**Fix:** Remove the `[FromKey]` and pick the key from the dictionary (`milks["Oat"]`), take the single service with `[FromKey("Oat")] IMilk milk`, or register a dictionary service under that key, which takes precedence over the synthesized one.

### AWT213

:::danger[Error]
A runtime-type dispatch dictionary (`IReadOnlyDictionary<Type, Func<TBase>>`) cannot be built: its value is not `Func<TBase>` over a non-generic interface, it carries a `[FromKey]`, a closing that implements `TBase` has more than one type argument, or the closings come from more than one generic interface, so a type argument does not identify one entry. See [Dispatch by runtime type](./05-resolution/04-keyed-dictionaries.md#dispatch-by-runtime-type).
:::

```csharp expect=AWT213
public interface IOrderHandler;
public interface IOrderHandler<T> : IOrderHandler;
public interface IRefundHandler<T> : IOrderHandler;   // a second family under the same base
public sealed class OrderPlacedHandler : IOrderHandler<OrderPlaced>;
public sealed class OrderPlacedRefund : IRefundHandler<OrderPlaced>;

public sealed class Counter(IReadOnlyDictionary<Type, Func<IOrderHandler>> handlers);   // typeof(OrderPlaced) is ambiguous

[Container]
[Singleton<OrderPlacedHandler, IOrderHandler<OrderPlaced>>]
[Singleton<OrderPlacedRefund, IRefundHandler<OrderPlaced>>]
[Singleton<Counter>]
public static partial class CoffeeShop;
```

**Fix:** Request `IReadOnlyDictionary<Type, Func<TBase>>` over a non-generic base interface, without `[FromKey]`, and let only one generic family with one type argument implement that base (here, do not derive `IRefundHandler<T>` from `IOrderHandler`).

## Contextual binding

### AWT167

:::warning[Warning]
A `WhenInjectedInto` contextual binding never applies because the named consumer has no unkeyed direct constructor parameter to redirect.
:::

```csharp expect=AWT167
public sealed class KitchenDisplay;   // has no IReceiptPrinter parameter to redirect

[Container]
[Singleton<ThermalPrinter, IReceiptPrinter>]
[Singleton<WidePrinter, IReceiptPrinter>(WhenInjectedInto = typeof(KitchenDisplay))]
[Singleton<KitchenDisplay>]
public static partial class CoffeeShop;
```

**Fix:** Let the consumer take the service as an unkeyed constructor parameter or `[Inject]` property (`KitchenDisplay(IReceiptPrinter printer)`), or remove the binding.

### AWT168

:::danger[Error]
A registration sets both `WhenInjectedInto` and `Key`. A contextual binding is reached only through its consumer, so the `Key` could never be selected by a `[FromKey]` and is silently dropped. Remove one of the two.
:::

```csharp expect=AWT168
[Container]
[Singleton<ThermalPrinter, IReceiptPrinter>]
[Singleton<WidePrinter, IReceiptPrinter>(WhenInjectedInto = typeof(DriveThroughRegister), Key = "wide")]   // Key is dropped
[Singleton<DriveThroughRegister>]
public static partial class CoffeeShop;
```

**Fix:** Remove either `WhenInjectedInto` or `Key`.

### AWT169

:::danger[Error]
Two different implementations set `WhenInjectedInto` for the same service and consumer, so both claim the one contextual slot for that consumer.
:::

```csharp expect=AWT169
[Container]
[Singleton<WidePrinter, IReceiptPrinter>(WhenInjectedInto = typeof(DriveThroughRegister))]
[Singleton<ThermalPrinter, IReceiptPrinter>(WhenInjectedInto = typeof(DriveThroughRegister))]   // two bindings for one consumer
[Singleton<DriveThroughRegister>]
public static partial class CoffeeShop;
```

**Fix:** Keep one contextual binding per service and consumer, and remove the other.

### AWT170

:::danger[Error]
A `Key` property or `[FromKey]` uses a constant whose type is not a supported key type. A resolution key must be a `string`, an `enum` value, or a `typeof(...)`.
:::

```csharp expect=AWT170,AWT101
public sealed class LatteRecipe([FromKey(5)] IMilk milk);   // int is not a supported key type

[Container]
[Singleton<OatMilk, IMilk>(Key = "Oat")]
[Singleton<LatteRecipe>]
public static partial class CoffeeShop;
```

**Fix:** Use a `string`, an enum value or a `typeof(...)` as the key: `[FromKey("Oat")]`.

## External services

### AWT175

:::danger[Error]
A type is declared `[ImportService]` (drawn from the host provider) but is also registered on the container. A type is either host-owned or Awaiten-owned, not both. For an open generic import such as `[ImportService(typeof(ILogger<>))]`, this is reported for each closing that a dependency routes to the host while the container also registers it.
:::

```csharp expect=AWT175
[Container]
[ImportService<IPaymentGateway>]                 // declared external
[Singleton<StripeGateway, IPaymentGateway>]      // …but is also registered, a contradiction
public static partial class CoffeeShop;
```

**Fix:** Remove the registration to keep the service host-owned, or remove the `[ImportService]` to build it in the graph.

### AWT176

:::warning[Warning]
A type declared `[ImportService]` is never consumed by any dependency in the graph, so the declaration is dead. For an open generic import, no closing of it is consumed. This is most often a stale or mistyped `[ImportService]`.
:::

```csharp expect=AWT176
[Container]
[ImportService<IPaymentGateway>]   // nothing in the graph depends on IPaymentGateway
[Singleton<Menu>]
public static partial class CoffeeShop;
```

**Fix:** Remove the declaration, or add the dependency that should consume it.

### AWT215

:::danger[Error]
The `typeof` argument of `[ImportService(typeof(...))]` or `[InjectProperty(typeof(...), name)]` is neither a closed type nor an unbound generic definition, so it names neither one type nor every closing of one generic type. That is a type nested in an unbound generic, such as `typeof(Recipe<>.Step)`, which C# accepts, or a partially open type, such as `typeof(Shelf<Order>.Slot<>)`, which C# rejects as well (CS7003). Pass a closed type such as `typeof(Recipe<Espresso>.Step)`, or an unbound generic definition with every type argument omitted such as `typeof(ILogger<>)` or `typeof(Shelf<>.Slot<>)`.
:::

```csharp expect=AWT215
public sealed class Recipe<T>
{
    public sealed class Step
    {
        public ITimeSystem? Time { get; set; }
    }
}

[Container]
[ImportService(typeof(Recipe<>.Step))]             // a type nested in an unbound generic is neither
[InjectProperty(typeof(Recipe<>.Step), "Time")]    // the same for [InjectProperty]
[Singleton<Menu>]
public static partial class CoffeeShop;
```

**Fix:** Pass a closed type such as `typeof(Recipe<Espresso>.Step)`, or an unbound generic definition with every type argument omitted.

## Composition boundary

These guard the line between your domain and the container. They are suppressible warnings, reported by the analyzer rather than the generator, so a deliberate exception can opt out in source.

### AWT134

:::warning[Warning]
A container-side composition attribute (a lifetime registration, `[Scan]`, `[Decorate]`, `[Composite]`, `[Import]`, `[ImportService]`, or `[InjectProperty]`) is applied to a class in an assembly that declares no `[Container]`. Composition belongs on the `[Container]` (or an imported `[Module]`); domain code should stay free of it. This is a best-effort guard: it stays silent in an assembly that also declares the `[Container]`, where the cross-assembly boundary is better enforced by an architecture test.
:::

```csharp no-compile
// A domain library that declares no [Container]:
[Singleton<EspressoMachine>]        // registration belongs on the [Container], not here
public sealed class EspressoMachine;
```

**Fix:** Move the attribute onto the `[Container]` or an imported `[Module]`. Where the attribute is deliberate, suppress the warning in source.

### AWT135

:::warning[Warning]
A resolver seam (`IAwaitenResolver`, `IAwaitenScope`, `IAwaitenRoot`, and the like) is injected into a type that is not the `[Container]` composition root. Resolving from the container at run time is the Service Locator anti-pattern: it hides the type's real dependencies and defeats the compile-time graph check. Inject the dependency you actually need instead, or, if the type is a host-integration adapter, suppress this on it with a justification. The typed fast paths `IAwaitenResolver<T>` and `IAwaitenAsyncResolver<T>` are single-service seams and are not reported.
:::

```csharp expect=AWT135
public sealed class Barista(IAwaitenResolver resolver)   // locates dependencies at run time
{
    public Cup Serve() => resolver.Resolve<Cup>();
}
```

**Fix:** Inject what the type actually needs: `Barista(Func<Cup> cups)` for a fresh cup per call, or `Barista(Cup cup)` for one. Suppress the warning only on a host-integration adapter, as described below.

Dispatching by a type known only at run time, like a mediator looking up the handler for `message.GetType()`, needs no resolver either: inject a [dispatch dictionary](./05-resolution/04-keyed-dictionaries.md#dispatch-by-runtime-type).

One kind of type is meant to hold a resolver: an adapter that bridges the container to a host's own dependency-injection surface, where holding it *is* the adaptation rather than a hidden run-time dependency. `AwaitenServiceProvider` is one, and anything you write against a framework's own registrar is another. This is an analyzer diagnostic, so suppress it in source, with the justification saying which adapter it is.

Below, `IServiceLocator` stands for whatever single-method resolution interface your framework declares, where returning `null` is how an adapter says "not mine". Both answers that are not a plain instance carry their weight: the [collection convention](./07-hosting/01-msdi-bridge.md#feature-detection) every framework expects, and the withheld reason that keeps a container's own failure from being reported as absence.

```csharp
using System.Diagnostics.CodeAnalysis;

[SuppressMessage("Awaiten", "AWT135:Service locator: a resolver interface is injected into a service",
    Justification = "This is a custom bridge adapter: holding the scope is the adaptation itself, not a hidden run-time dependency.")]
internal sealed class AwaitenServiceLocator(IAwaitenRoot container, IAwaitenScope scope) : IServiceLocator
{
    public object? Resolve(Type? type)
    {
        if (type is null)
        {
            return null;
        }

        if (scope.TryResolve(type, out object? instance))
        {
            return instance;
        }

        // Reported as absent, the framework would bind it from elsewhere and fail far from the cause.
        if (container.GetResolvability(type, null).Reason is { } reason)
        {
            throw new InvalidOperationException(reason);
        }

        return EmptyCollectionElement(type) is { } elementType
            ? Array.CreateInstance(elementType, 0)
            : null;
    }

    /// <summary>
    ///     The element type to answer with an empty sequence rather than <see langword="null" />, because a
    ///     framework uses a collection as an extension point and "no handlers" still has to enumerate. A value
    ///     element would need an array type native AOT does not generate; a resolvable one has members an empty
    ///     sequence would drop.
    /// </summary>
    private Type? EmptyCollectionElement(Type type)
    {
        if (!type.IsConstructedGenericType || type.GetGenericTypeDefinition() != typeof(IEnumerable<>))
        {
            return null;
        }

        Type elementType = type.GenericTypeArguments[0];
        return elementType.IsValueType
               || container.GetResolvability(elementType, null).Status != AwaitenResolvabilityStatus.NotRegistered
            ? null
            : elementType;
    }
}
```

`AwaitenServiceProvider` is the same shape against MS.DI's `IServiceProvider`, so read it alongside this if your framework's surface is keyed or asks resolvability questions.

Suppress it on the adapter, never project-wide: the diagnostic is right about every other type.

### AWT205

:::warning[Warning]
User code constructs an `Owned<T>` with `new`. The handle disposes the scope it is given, so one built over the container root disposes the root, and one built over any other scope ends that scope behind its owner's back. Only the container creates `Owned<T>`, over the throwaway scope it built the value in: resolve `Owned<T>` or inject `Func<Owned<T>>` instead. The generated container's own handles are not reported, and a deliberate construction, such as a test double, can suppress the warning in source.
:::

```csharp expect=AWT205
Owned<Cup> cup = new Owned<Cup>(root, new Cup());   // disposing it disposes the root
Owned<Cup> owned = root.Resolve<Owned<Cup>>();      // the container builds the handle
```

**Fix:** Resolve `Owned<T>`, as the second line does, or inject `Func<Owned<T>>`. Suppress the warning in source only for a deliberate construction, such as a test double.
