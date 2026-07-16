---
title: Diagnostics
toc_max_heading_level: 2
---

# Diagnostics

Awaiten checks your wiring while it builds. A mistake that other containers would surface at startup, or on the first resolve in production, is a compile error here. The barista never reaches for a cup that is not there.

Every diagnostic has an `AWT###` id. Errors stop the build. Warnings point at something that is legal but probably not what you meant. This page is the full catalogue, one entry per id with a concrete example that triggers it. The feature pages link here for the codes they mention.

The examples reuse a few coffee-shop types (`Grinder`, `EspressoMachine`, `Order`, `Cup`, `IMilk`, `IBrewer`, and so on). Only the detail that trips the diagnostic is spelled out in each snippet.

## Graph correctness

These check that the object graph can actually be built.

### AWT101

:::danger[Error]
A required dependency has no registration.
:::

```csharp
public sealed class Cup(Grinder grinder);

[Container]
[Transient<Cup>]        // Grinder is never registered
public static partial class CoffeeShop;
```

### AWT102

:::danger[Error]
A dependency cycle exists in the object graph.
:::

```csharp
public sealed class Till(Printer printer);
public sealed class Printer(Till till);   // Till needs Printer needs Till

[Container]
[Transient<Till>]
[Transient<Printer>]
public static partial class CoffeeShop;
```

### AWT103

:::danger[Error]
An implementation type is abstract or an interface.
:::

```csharp
public interface IBrewer;

[Container]
[Singleton<IBrewer>]    // IBrewer is an interface, not something to construct
public static partial class CoffeeShop;
```

### AWT104

:::danger[Error]
An implementation type has no accessible constructor.
:::

```csharp
public sealed class Grinder
{
    private Grinder() { }   // only a private constructor
}

[Container]
[Singleton<Grinder>]
public static partial class CoffeeShop;
```

### AWT105

:::danger[Error]
A singleton captures a shorter-lived scoped dependency.
:::

```csharp
public sealed class EspressoMachine(Order order);   // singleton needs a scoped Order

[Container]
[Singleton<EspressoMachine>]
[Scoped<Order>]
public static partial class CoffeeShop;
```

### AWT107

:::danger[Error]
An implementation is registered with conflicting lifetimes.
:::

```csharp
[Container]
[Singleton<Grinder>]
[Transient<Grinder>]    // same implementation, two lifetimes
public static partial class CoffeeShop;
```

### AWT111

:::danger[Error]
An implementation is registered with conflicting production strategies.
:::

```csharp
[Container]
[Singleton<Grinder>(Factory = nameof(MakeGrinder))]
[Singleton<Grinder>]    // one via factory, one via constructor
public static partial class CoffeeShop
{
    private static Grinder MakeGrinder() => new();
}
```

### AWT116

:::danger[Error]
A `[Container]` class is not declared static.
:::

```csharp
[Container]
public partial class CoffeeShop;   // must be static partial
```

### AWT117

:::danger[Error]
Two registrations share the same service type and key.
:::

```csharp
[Container]
[Singleton<OatMilk, IMilk>(Key = "Oat")]
[Singleton<SoyMilk, IMilk>(Key = "Oat")]   // same service and key
public static partial class CoffeeShop;
```

### AWT148

:::warning[Warning]
Two overridable default registrations provide the same service ambiguously.
:::

```csharp
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

## Async safety

These keep async-initialized services from being reached before they are ready.

### AWT106

:::warning[Warning]
A synchronous factory's body provably produces an `IAsyncInitializable` concrete type its declared return type hides.
:::

```csharp
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

### AWT119

:::danger[Error]
A synchronous `Func`/`Lazy`/`Owned` relationship targets an async-initialized service.
:::

```csharp
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

### AWT120

:::danger[Error]
A synchronous `Func`/`Lazy`/`Owned` relationship reaches an async-tainted service transitively.
:::

```csharp
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

### AWT122

:::danger[Error]
A collection dependency has an async-tainted member but is materialized synchronously.
:::

```csharp
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

### AWT156

:::warning[Warning]
A generated `Root`/`Scope` is disposed synchronously although its container owns a service that implements `IAsyncDisposable` but not `IDisposable`.
:::

```csharp
public sealed class Boiler : IAsyncDisposable
{
    public ValueTask DisposeAsync() => default;
}

[Container]
[Singleton<Boiler>]
public static partial class CoffeeShop;

using (var shop = new CoffeeShop.Root()) { }   // sync dispose cannot tear down Boiler; use await using
```

### AWT161

:::danger[Error]
An eager singleton is async-initialized and cannot be constructed synchronously at container build time.
:::

```csharp
public sealed class EspressoMachine : IAsyncInitializable
{
    public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;
}

[Container]
[Singleton<EspressoMachine>(Eager = true)]   // async service cannot be built eagerly at construction
public static partial class CoffeeShop;
```

## Factories and instances

### AWT108

:::danger[Error]
A `Factory` registration names a member that is not a usable factory method.
:::

```csharp
[Container]
[Singleton<Grinder>(Factory = nameof(Count))]   // Count is a field, not a factory method
public static partial class CoffeeShop
{
    private static int Count = 3;
}
```

### AWT109

:::danger[Error]
An `Instance` registration names a member that is not a usable instance member.
:::

```csharp
[Container]
[Singleton<Grinder>(Instance = nameof(MakeGrinder))]   // MakeGrinder is a method, not an instance member
public static partial class CoffeeShop
{
    private static Grinder MakeGrinder() => new();
}
```

### AWT110

:::danger[Error]
A registration sets both `Factory` and `Instance`.
:::

```csharp
[Container]
[Singleton<Grinder>(Factory = nameof(MakeGrinder), Instance = nameof(Shared))]
public static partial class CoffeeShop
{
    private static Grinder MakeGrinder() => new();
    private static Grinder Shared { get; } = new();
}
```

### AWT112

:::danger[Error]
A `Factory` registration names an overloaded method.
:::

```csharp
[Container]
[Singleton<Grinder>(Factory = nameof(MakeGrinder))]   // MakeGrinder is overloaded
public static partial class CoffeeShop
{
    private static Grinder MakeGrinder() => new();
    private static Grinder MakeGrinder(int burrs) => new();
}
```

### AWT162

:::danger[Error]
A `[RequestingType]` factory parameter is not of type `System.Type`.
:::

```csharp
[Container]
[Transient<ILogger>(Factory = nameof(CreateLogger))]
public static partial class CoffeeShop
{
    private static Logger CreateLogger([RequestingType] string consumer) => new(consumer);   // must be Type
}
```

### AWT163

:::danger[Error]
A factory has both a `[RequestingType]` parameter and an `[Arg]` runtime-argument parameter.
:::

```csharp
[Container]
[Transient<ILogger>(Factory = nameof(CreateLogger))]
public static partial class CoffeeShop
{
    private static Logger CreateLogger([RequestingType] Type consumer, [Arg] string name)
        => new(consumer, name);
}
```

### AWT186

:::danger[Error]
An `Owned<T>` relationship (or its `Func`/`Task` forms) targets a service produced by a requesting-type factory. Such a factory is called per consumer and decides its own disposal, so it has no owner scope to build the owned target into. Consume the service directly, or through `Func<T>` / `Lazy<T>`.
:::

```csharp
public sealed class Barista(Owned<ILogger> logger);   // Owned<T> over a requesting-type factory

[Container]
[Transient<ILogger>(Factory = nameof(CreateLogger))]
[Transient<Barista>]
public static partial class CoffeeShop
{
    private static Logger CreateLogger([RequestingType] Type? consumer) => new(consumer?.Name ?? "<root>");
}
```

## Lifecycle hooks

### AWT164

:::danger[Error]
An `OnActivated`/`OnRelease` registration names a member that is not a usable lifecycle hook.
:::

```csharp
[Container]
[Singleton<EspressoMachine>(OnActivated = nameof(Ready))]   // Ready is a field, not a hook method
public static partial class CoffeeShop
{
    private static bool Ready = false;
}
```

### AWT165

:::danger[Error]
An `OnActivated`/`OnRelease` lifecycle hook is set on a pre-built `Instance` registration, which the container does not own.
:::

```csharp
[Container]
[Singleton<MenuBoard>(Instance = nameof(Menu), OnRelease = nameof(Archive))]
public static partial class CoffeeShop
{
    private static MenuBoard Menu { get; } = new();
    private static void Archive(object board) { }   // the container does not own an Instance
}
```

### AWT166

:::danger[Error]
An implementation is registered with conflicting `OnActivated`/`OnRelease`/`Eager` directives that coalescing would silently drop.
:::

```csharp
[Container]
[Singleton<EspressoMachine, IWarmable>(OnActivated = nameof(Calibrate))]
[Singleton<EspressoMachine, IMachine>(OnActivated = nameof(Preheat))]   // two hooks for one implementation
public static partial class CoffeeShop
{
    private static void Calibrate(object m) { }
    private static void Preheat(object m) { }
}
```

### AWT189

:::danger[Error]
A lifecycle hook parameter (after the instance) is marked `[Arg]`, but a hook resolves its parameters from the graph.
:::

```csharp
[Container]
[Singleton<EspressoMachine>(OnActivated = nameof(Calibrate))]
public static partial class CoffeeShop
{
    private static void Calibrate(EspressoMachine machine, [Arg] int count) { }   // no Func<…> call site supplies an [Arg]
}
```

### AWT190

:::danger[Error]
A lifecycle hook (`OnActivated` / `OnRelease`) names an overloaded method, so the container cannot choose which one to call.
:::

The container reaches a hook by simple name, so two accepting overloads leave the choice (and the graph dependencies the extra parameters resolve) order-dependent. Give the hook a unique name, exactly as a factory method must be unambiguous.

```csharp
[Container]
[Singleton<EspressoMachine>(OnActivated = nameof(Calibrate))]
public static partial class CoffeeShop
{
    private static void Calibrate(EspressoMachine machine) { }
    private static void Calibrate(EspressoMachine machine, Settings settings) { }   // which one runs?
}
```

### AWT191

:::danger[Error]
An `OnRelease` hook parameter is a `Func`/`Lazy` relationship, which would defer resolution past the owner's teardown.
:::

A release dependency is captured at construction, but a `Func<T>` or `Lazy<T>` captures only a resolver delegate. The hook runs while its owner is being disposed, so invoking the delegate there always throws. Take the dependency directly instead: it is resolved at construction and, released in reverse creation order, still alive when the hook uses it. An `OnActivated` hook may take `Func`/`Lazy` parameters freely, since it runs while the owner is alive.

```csharp
[Container]
[Singleton<BufferPool>]
[Transient<Buffer>(OnRelease = nameof(ReturnToPool))]
public static partial class CoffeeShop
{
    private static void ReturnToPool(Buffer buffer, Func<BufferPool> pool) { }   // pool() would throw during teardown; take BufferPool directly
}
```

## Runtime arguments

### AWT113

:::danger[Error]
A `Func<TArg...,T>` or `Func<TArg...,Task<T>>` relationship's runtime arguments do not match the service's `[Arg]` parameters.
:::

```csharp
public sealed class Cup([Arg] string size);

[Container]
[Transient<Cup>]
public static partial class CoffeeShop;

var make = shop.Resolve<Func<int, Cup>>();   // the [Arg] is string, not int
```

### AWT114

:::danger[Error]
A service with `[Arg]` parameters is registered with a non-Transient lifetime.
:::

```csharp
public sealed class Cup([Arg] string size);

[Container]
[Singleton<Cup>]    // [Arg] services must be transient
public static partial class CoffeeShop;
```

### AWT115

:::danger[Error]
A service with `[Arg]` parameters is required as a plain, `Lazy<T>`, or `Task<T>` dependency instead of a `Func<TArg...,T>`.
:::

```csharp
public sealed class Cup([Arg] string size);
public sealed class Barista(Cup cup);   // must ask for Func<string, Cup>

[Container]
[Transient<Cup>]
[Singleton<Barista>]
public static partial class CoffeeShop;
```

### AWT137

:::danger[Error]
An injected property is marked `[Arg]`.
:::

```csharp
public sealed class Barista
{
    [Inject, Arg] public string? Name { get; set; }   // [Arg] is for constructor and factory parameters
}

[Container]
[Transient<Barista>]
public static partial class CoffeeShop;
```

## Ownership and lifetime safety

### AWT118

:::danger[Error]
A root-owned instance holds a `Func` or `Func<…,Task<T>>` over a disposable build-on-demand service. Under strict lifetime safety (the default) this is a non-suppressible error; under `LifetimeSafety.Loose` it relaxes to a suppressible warning.
:::

```csharp
public sealed class BrewSession : IDisposable { public void Dispose() { } }
public sealed class Counter(Func<BrewSession> sessions);   // each call strands a disposable on the root

[Container]                          // strict lifetime safety (the default)
[Transient<BrewSession>]
[Singleton<Counter>]
public static partial class CoffeeShop;
```

### AWT121

:::danger[Error]
An `Owned<T>` disposal handle is requested through a `Lazy<Owned<T>>` or `Lazy<Task<Owned<T>>>` relationship.
:::

```csharp
public sealed class BrewSession : IDisposable { public void Dispose() { } }
public sealed class Counter(Lazy<Owned<BrewSession>> sessions);   // Owned cannot hide behind Lazy

[Container]
[Transient<BrewSession>]
[Singleton<Counter>]
public static partial class CoffeeShop;
```

### AWT192

:::danger[Error]
`SuppressDisposal` is set on a pre-built `Instance`, which the container does not own or dispose, so it has no effect. Remove it, or register the type for construction (by constructor or `Factory`) instead of as an `Instance`.
:::

```csharp
public sealed class Boiler : IDisposable { public void Dispose() { } }

[Container]
[Singleton<Boiler>(Instance = nameof(Shared), SuppressDisposal = true)]   // an Instance is never disposed anyway
public static partial class CoffeeShop
{
    private static Boiler Shared { get; } = new();
}
```

## Decorators and composites

### AWT123

:::danger[Error]
A `[Decorate]` names a service with no registration to decorate. Also raised when an open generic `[Decorate(typeof(D<>), typeof(IService<>))]` matches no closing of its service in the graph.
:::

```csharp
public sealed class LoggingTerminal(IPaymentTerminal inner) : IPaymentTerminal;

[Container]
[Decorate<LoggingTerminal, IPaymentTerminal>]   // nothing registers IPaymentTerminal
public static partial class CoffeeShop;
```

### AWT124

:::danger[Error]
A decorator has no single constructor parameter assignable to the decorated service type.
:::

```csharp
public sealed class LoggingTerminal : IPaymentTerminal;   // no IPaymentTerminal inner parameter

[Container]
[Singleton<CardTerminal, IPaymentTerminal>]
[Decorate<LoggingTerminal, IPaymentTerminal>]
public static partial class CoffeeShop;
```

### AWT130

:::danger[Error]
A `[Composite]` implementation has no collection parameter of the composed service to fan out to.
:::

```csharp
public sealed class CompositeReceiptChannel : IReceiptChannel;   // no IEnumerable<IReceiptChannel> parameter

[Container]
[Transient<PrinterChannel, IReceiptChannel>]
[Composite<CompositeReceiptChannel, IReceiptChannel>]
public static partial class CoffeeShop;
```

### AWT131

:::warning[Warning]
A `[Composite]` type is also registered as an ordinary member of the service it composes.
:::

```csharp
public sealed class CompositeReceiptChannel(IEnumerable<IReceiptChannel> channels) : IReceiptChannel;

[Container]
[Transient<CompositeReceiptChannel, IReceiptChannel>]   // registered as a normal member too
[Composite<CompositeReceiptChannel, IReceiptChannel>]
public static partial class CoffeeShop;
```

### AWT132

:::danger[Error]
More than one `[Composite]` names the same service.
:::

```csharp
[Container]
[Composite<CompositeReceiptChannel, IReceiptChannel>]
[Composite<BackupReceiptChannel, IReceiptChannel>]   // two composites for one service
public static partial class CoffeeShop;
```

### AWT133

:::danger[Error]
A `[Composite]`'s collection parameter is of a base type of the composed service, not the composed service itself.
:::

```csharp
// IReceiptChannel : IChannel
public sealed class CompositeReceiptChannel(IEnumerable<IChannel> channels) : IReceiptChannel;   // IChannel is a base type

[Container]
[Transient<PrinterChannel, IReceiptChannel>]
[Composite<CompositeReceiptChannel, IReceiptChannel>]
public static partial class CoffeeShop;
```

## Open generics

### AWT125

:::danger[Error]
An open generic registration's implementation and service have different arity. The open `[Decorate]`/`[Composite]` `typeof` forms are held to the same rule (decorator/composite arity must match the service).
:::

```csharp
[Container]
[Transient(typeof(Repository<>), typeof(IRepository<,>))]   // one type parameter vs two
public static partial class CoffeeShop;
```

### AWT126

:::danger[Error]
A required closed type violates the open generic implementation's type-parameter constraints, so the dependency cannot be satisfied. (For the open `[Decorate]`/`[Composite]` case, where the base service still resolves, see the warning [AWT171](#awt171).)
:::

```csharp
public sealed class Repository<T> : IRepository<T> where T : class;
public sealed class Ledger(IRepository<int> repo);   // int is not a class

[Container]
[Transient(typeof(Repository<>), typeof(IRepository<>))]
[Singleton<Ledger>]
public static partial class CoffeeShop;
```

### AWT171

:::warning[Warning]
The decorator/composite counterpart to [AWT126](#awt126): a closing of an open generic `[Decorate]`/`[Composite]` cannot be constructed because its type arguments violate the decorator's or composite's type-parameter constraints. A warning, not an error, because the base service still resolves. That one closing is left as-is (undecorated/unfronted), and the remaining closings are decorated/composed as usual.
:::

```csharp
public interface IHandler<T> { }
public sealed class Handler<T> : IHandler<T> { }
public sealed class Logging<T>(IHandler<T> inner) : IHandler<T> where T : class;   // reference types only

[Container]
[Transient(typeof(Handler<>), typeof(IHandler<>))]
[Transient<Root>]
[Decorate(typeof(Logging<>), typeof(IHandler<>))]   // IHandler<int> can't take Logging<int>: warned and skipped
public static partial class CoffeeShop;
```

### AWT127

:::danger[Error]
The `typeof`-argument form of a lifetime, `[Decorate]` or `[Composite]` attribute must receive an unbound open generic type.
:::

```csharp
[Container]
[Transient(typeof(Repository<Order>), typeof(IRepository<Order>))]   // closed, not open
public static partial class CoffeeShop;
```

### AWT128

:::danger[Error]
An open generic implementation does not expose its service with type parameters in declaration order. The open `[Decorate]`/`[Composite]` `typeof` forms are held to the same rule.
:::

```csharp
public sealed class Map<TKey, TValue> : IMap<TValue, TKey>;   // parameters swapped

[Container]
[Transient(typeof(Map<,>), typeof(IMap<,>))]
public static partial class CoffeeShop;
```

### AWT129

:::danger[Error]
Open generic expansion nested too deep, indicating an unbounded generic recursion.
:::

```csharp
public sealed class Box<T>(Box<Box<T>> inner);   // Box<Order> needs Box<Box<Order>> needs ...
public sealed class Consumer(Box<Order> box);

[Container]
[Transient(typeof(Box<>))]
[Singleton<Consumer>]
public static partial class CoffeeShop;
```

## Property injection

### AWT136

:::danger[Error]
An `[Inject]` property has no set or init accessor the container can assign through.
:::

```csharp
public sealed class Barista
{
    [Inject] public ITimeSystem? Time { get; }   // get-only, nothing to assign
}

[Container]
[Transient<Barista>]
[Singleton<RealTimeSystem, ITimeSystem>]
public static partial class CoffeeShop;
```

### AWT144

:::danger[Error]
An `[Inject(Deferred = true)]` property is init-only or required rather than assignable after construction.
:::

```csharp
public sealed class Node
{
    [Inject(Deferred = true)] public Node? Peer { get; init; }   // deferred needs a plain set
}

[Container]
[Singleton<Node>]
public static partial class CoffeeShop;
```

### AWT145

:::danger[Error]
A deferred property cycle consists entirely of transients and cannot terminate.
:::

```csharp
public sealed class A { [Inject(Deferred = true)] public B? B { get; set; } }
public sealed class B { [Inject(Deferred = true)] public A? A { get; set; } }

[Container]
[Transient<A>]    // all transient: a fresh instance every step, so the cycle never closes
[Transient<B>]
public static partial class CoffeeShop;
```

### AWT146

:::danger[Error]
A deferred property cycle includes an async-initialized service and cannot terminate.
:::

```csharp
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

### AWT147

:::danger[Error]
A deferred property cycle still traverses a construction-time edge that duplicates a cached participant or recurses forever.
:::

```csharp
public sealed class A(B b);                                       // construction-time edge into the cycle
public sealed class B { [Inject(Deferred = true)] public A? A { get; set; } }

[Container]
[Transient<A>]
[Transient<B>]
public static partial class CoffeeShop;
```

### AWT157

:::danger[Error]
An `[Inject(Optional = true)]` property is required and cannot be omitted from the object initializer.
:::

```csharp
public sealed class Barista
{
    [Inject(Optional = true)] public required ITimeSystem Time { get; set; }   // required cannot be omitted
}

[Container]
[Transient<Barista>]
public static partial class CoffeeShop;
```

### AWT158

:::warning[Warning]
An `[Inject(Optional = true)]` property is init-only, so an unregistered dependency leaves it permanently at its default.
:::

```csharp
public sealed class Barista
{
    [Inject(Optional = true)] public ITimeSystem? Time { get; init; }   // init-only stays default forever
}

[Container]
[Transient<Barista>]
public static partial class CoffeeShop;
```

### AWT177

:::danger[Error]
An `[InjectProperty<T>]` names a member that is not a settable property on the implementation: an unknown name, a field or method, or a read-only property.
:::

```csharp
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

### AWT178

:::danger[Error]
An `[InjectProperty<T>]` targets an implementation produced by a `Factory` or `Instance` registration. Such an instance is built whole by its source, so there is no object initializer for the container to fill.
:::

```csharp
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

### AWT179

:::warning[Warning]
Two `[InjectProperty<T>]` entries name the same property of the same implementation. The property is filled once; the duplicate is ignored.
:::

```csharp
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

### AWT180

:::warning[Warning]
An `[InjectProperty<T>]` names an implementation that has no container-constructed registration, so the entry is never applied: the type is unregistered, or it is an open generic no consumer closed.
:::

```csharp
public sealed class Barista
{
    public ITimeSystem? Time { get; set; }
}

[Container]
[Singleton<RealTimeSystem, ITimeSystem>]
[InjectProperty<Barista>(nameof(Barista.Time))]   // Barista itself is never registered
public static partial class CoffeeShop;
```

### AWT181

:::warning[Warning]
A property carries both `[Inject]` and a container-side `[InjectProperty<T>]` entry. The property is filled once, from `[Inject]`, so the entry's `Optional`/`Deferred`/`Key` are ignored.
:::

```csharp
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

## Scanning

### AWT138

:::warning[Warning]
A `[Scan]` matched no concrete type assignable to its marker.
:::

```csharp
public interface IPlugin;   // no implementations exist

[Container]
[Scan<IPlugin>]
public static partial class CoffeeShop;
```

### AWT139

:::warning[Warning]
A `[Scan(As = ScanAs.Marker)]` matched a type with no assignable interface.
:::

```csharp
public abstract class Report;
public sealed class QuarterlyReport : Report;   // a Report, but implements no interface

[Container]
[Scan(typeof(Report), As = ScanAs.Marker)]
public static partial class CoffeeShop;
```

### AWT140

:::warning[Warning]
A `[Scan(InAssembliesOf = …)]` named an assembly with no candidate types.
:::

```csharp
[Container]
[Scan(typeof(IPlugin), InAssembliesOf = new[] { typeof(string) })]   // that assembly has no IPlugin types
public static partial class CoffeeShop;
```

### AWT141

:::warning[Warning]
A `[Scan(SkipUnconstructable = true)]` match the container cannot construct is skipped.
:::

```csharp
public sealed class LegacyPlugin : IPlugin
{
    private LegacyPlugin() { }   // no usable constructor, so it is skipped
}

[Container]
[Scan(typeof(IPlugin), SkipUnconstructable = true)]
public static partial class CoffeeShop;
```

### AWT142

:::warning[Warning]
Scans register one implementation with conflicting lifetimes.
:::

```csharp
public sealed class Widget : IPlugin, IHandler;   // matched by both scans

[Container]
[Scan(typeof(IPlugin), Lifetime = AwaitenLifetime.Singleton)]
[Scan(typeof(IHandler), Lifetime = AwaitenLifetime.Transient)]   // two lifetimes for Widget
public static partial class CoffeeShop;
```

### AWT143

:::danger[Error]
A `[Scan(InAssembliesOf = …)]` resolved to no assembly at all.
:::

```csharp
[Container]
[Scan(typeof(IPlugin), InAssembliesOf = new Type[0])]   // no assemblies to scan
public static partial class CoffeeShop;
```

### AWT172

:::warning[Warning]
A `[Scan]`'s name, namespace or exclude filters removed every marker-assignable match.
:::

```csharp
public sealed class GrinderPlugin : IPlugin;   // assignable, but filtered out by the pattern below

[Container]
[Scan<IPlugin>(NamePatterns = ["*Handler"])]   // no IPlugin ends in "Handler", so nothing registers
public static partial class CoffeeShop;
```

### AWT173

:::warning[Warning]
A `[Scan]` exclusion (an `Exclude` type or a `!`-prefixed pattern) matched no candidate.
:::

```csharp
public sealed class GrinderPlugin : IPlugin;

[Container]
[Scan<IPlugin>(Exclude = [typeof(RenamedPlugin)])]   // RenamedPlugin is no longer a candidate: stale
public static partial class CoffeeShop;
```

### AWT174

:::warning[Warning]
A `[Scan]` include pattern matches every candidate, so it does not narrow the scan.
:::

```csharp
[Container]
[Scan<IPlugin>(NamePatterns = ["*"])]   // "*" matches every name; drop it
public static partial class CoffeeShop;
```

### AWT182

:::warning[Warning]
A `[Scan(As = ScanAs.MatchingInterface)]` matched a type that implements no interface named `I` + its own name.
:::

```csharp
public interface IEspresso;
public sealed class Espresso : IEspresso;
public sealed class Ristretto : IEspresso;   // no IRistretto, so it is not registered

[Container]
[Scan<IEspresso>(As = ScanAs.MatchingInterface)]
public static partial class CoffeeShop;
```

### AWT183

:::danger[Error]
A markerless `[Scan]` includes the `Marker` exposure, or declares no scoping filter.
:::

```csharp
[Container]
[Scan(As = ScanAs.MatchingInterface)]   // markerless, but no NamePatterns/NamespacePatterns/InAssembliesOf
public static partial class CoffeeShop;
```

### AWT184

:::warning[Warning]
A markerless `[Scan]` matched candidate types but registered none of them.
:::

```csharp
[Container]
// Nothing under CoffeeShop.Legacy follows the I + name convention, so nothing registers.
[Scan(As = ScanAs.MatchingInterface, NamespacePatterns = ["CoffeeShop.Legacy.**"])]
public static partial class CoffeeShop;
```

### AWT185

:::danger[Error]
A `[Scan]`'s `As` resolved to no `ScanAs` flag, so it would register nothing (usually `&` written for `|`).
:::

```csharp
[Container]
[Scan<IDrink>(As = ScanAs.Self & ScanAs.Marker)]   // & is empty; use | to combine flags
public static partial class CoffeeShop;
```

### AWT187

:::warning[Warning]
A `[Scan(As = ScanAs.MatchingInterface)]` matched a type implementing several same-named convention interfaces, so it registers under each of them.
:::

```csharp
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

### AWT188

:::warning[Warning]
A scan match's only exposure interface is inaccessible to the generated container, so the match is not registered.
:::

```csharp
// In a referenced assembly: the convention interface is internal.
internal interface IRoaster;
public sealed class Roaster : IRoaster, IEquipment;

// Registering Roaster under IRoaster would not compile in the container's assembly.
[Container]
[Scan<IEquipment>(As = ScanAs.MatchingInterface, InAssembliesOf = [typeof(IEquipment)])]
public static partial class CoffeeShop;
```

## Modules

### AWT149

:::danger[Error]
An `[Import]` names a type that is not marked `[Module]`.
:::

```csharp
public static class PaymentHelpers;   // not marked [Module]

[Container]
[Import(typeof(PaymentHelpers))]
public static partial class CoffeeShop;
```

### AWT150

:::danger[Error]
An imported module has its own `[Import]`, which is not followed.
:::

```csharp
[Module]
[Import(typeof(BaseModule))]   // a nested import is not followed
public static class PaymentModule;

[Container]
[Import(typeof(PaymentModule))]
public static partial class CoffeeShop;
```

### AWT151

:::warning[Warning]
An imported module declares no registrations.
:::

```csharp
[Module]
public static class EmptyModule;   // nothing to contribute

[Container]
[Import(typeof(EmptyModule))]
public static partial class CoffeeShop;
```

### AWT152

:::danger[Error]
An imported `[Module]` class is not declared static.
:::

```csharp
[Module]
public class PaymentModule;   // must be static
```

### AWT153

:::danger[Error]
A module `Factory`/`Instance` member is not accessible from the generated container.
:::

```csharp
[Module]
[Singleton<Grinder>(Factory = nameof(MakeGrinder))]
public static class EquipmentModule
{
    private static Grinder MakeGrinder() => new();   // private, so the container cannot call it
}
```

### AWT154

:::danger[Error]
An imported module declares a `[Scan]`, which is not collected from modules.
:::

```csharp
[Module]
[Scan<IDrink>]   // scans are not gathered from modules
public static class MenuModule;

[Container]
[Import(typeof(MenuModule))]
public static partial class CoffeeShop;
```

### AWT155

:::warning[Warning]
Two imported modules strongly register the same service with different implementations.
:::

```csharp
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

## Keyed collections

### AWT159

:::danger[Error]
A keyed dictionary (`IReadOnlyDictionary<TKey, TService>`) has a key type that is neither `string` nor an enum, or one whose keyed registrations do not all match it.
:::

```csharp
public sealed class Router(IReadOnlyDictionary<int, IMilk> milks);   // the key must be string or an enum

[Container]
[Singleton<OatMilk, IMilk>(Key = "Oat")]
[Singleton<Router>]
public static partial class CoffeeShop;
```

### AWT160

:::danger[Error]
A `[FromKey]` is applied to a synthesized keyed collection (`IReadOnlyDictionary<string, TService>`), which resolves every key.
:::

```csharp
public sealed class Router([FromKey("Oat")] IReadOnlyDictionary<string, IMilk> milks);   // the map already holds every key

[Container]
[Singleton<OatMilk, IMilk>(Key = "Oat")]
[Singleton<Router>]
public static partial class CoffeeShop;
```

## Contextual binding

### AWT167

:::warning[Warning]
A `WhenInjectedInto` contextual binding never applies because the named consumer has no unkeyed direct constructor parameter to redirect.
:::

```csharp
public sealed class KitchenDisplay;   // has no IReceiptPrinter parameter to redirect

[Container]
[Singleton<ThermalPrinter, IReceiptPrinter>]
[Singleton<WidePrinter, IReceiptPrinter>(WhenInjectedInto = typeof(KitchenDisplay))]
[Singleton<KitchenDisplay>]
public static partial class CoffeeShop;
```

### AWT168

:::danger[Error]
A registration sets both `WhenInjectedInto` and `Key`. A contextual binding is reached only through its consumer, so the `Key` could never be selected by a `[FromKey]` and is silently dropped. Remove one of the two.
:::

```csharp
[Container]
[Singleton<ThermalPrinter, IReceiptPrinter>]
[Singleton<WidePrinter, IReceiptPrinter>(WhenInjectedInto = typeof(DriveThroughRegister), Key = "wide")]   // Key is dropped
[Singleton<DriveThroughRegister>]
public static partial class CoffeeShop;
```

### AWT169

:::danger[Error]
Two different implementations set `WhenInjectedInto` for the same service and consumer, so both claim the one contextual slot for that consumer.
:::

```csharp
[Container]
[Singleton<WidePrinter, IReceiptPrinter>(WhenInjectedInto = typeof(DriveThroughRegister))]
[Singleton<ThermalPrinter, IReceiptPrinter>(WhenInjectedInto = typeof(DriveThroughRegister))]   // two bindings for one consumer
[Singleton<DriveThroughRegister>]
public static partial class CoffeeShop;
```

### AWT170

:::danger[Error]
A `[Key]` or `[FromKey]` uses a constant whose type is not a supported key type. A resolution key must be a `string`, an `enum` value, or a `typeof(...)`.
:::

```csharp
public sealed class LatteRecipe([FromKey(5)] IMilk milk);   // int is not a supported key type

[Container]
[Singleton<OatMilk, IMilk>(Key = "Oat")]
[Singleton<LatteRecipe>]
public static partial class CoffeeShop;
```

## External services

### AWT175

:::danger[Error]
A type is declared `[ImportService<T>]` (drawn from the host provider) but is also registered on the container. A type is either host-owned or Awaiten-owned, not both.
:::

```csharp
[Container]
[ImportService<IPaymentGateway>]                 // declared external
[Singleton<StripeGateway, IPaymentGateway>]      // …but is also registered, a contradiction
public static partial class CoffeeShop;
```

### AWT176

:::warning[Warning]
A type declared `[ImportService<T>]` is never consumed by any dependency in the graph, so the declaration is dead. This is most often a stale or mistyped `[ImportService<T>]`.
:::

```csharp
[Container]
[ImportService<IPaymentGateway>]   // nothing in the graph depends on IPaymentGateway
[Singleton<Menu>]
public static partial class CoffeeShop;
```

## Composition boundary

These guard the line between your domain and the container. They are suppressible warnings, reported by the analyzer rather than the generator, so a deliberate exception can opt out in source.

### AWT134

:::warning[Warning]
A container-side composition attribute (a lifetime registration, `[Scan]`, `[Decorate]`, `[Composite]`, `[Import]`, `[ImportService]`, or `[InjectProperty]`) is applied to a class in an assembly that declares no `[Container]`. Composition belongs on the `[Container]` (or an imported `[Module]`); domain code should stay free of it. This is a best-effort guard: it stays silent in an assembly that also declares the `[Container]`, where the cross-assembly boundary is better enforced by an architecture test.
:::

```csharp
// A domain library that declares no [Container]:
[Singleton<EspressoMachine>]        // registration belongs on the [Container], not here
public sealed class EspressoMachine;
```

### AWT135

:::warning[Warning]
A resolver seam (`IAwaitenResolver`, `IAwaitenScope`, `IAwaitenRoot`, and the like) is injected into a type that is not the `[Container]` composition root. Resolving from the container at run time is the Service Locator anti-pattern: it hides the type's real dependencies and defeats the compile-time graph check. Inject the dependency you actually need instead. The typed fast-path `IAwaitenResolver<T>` is a single-service seam and is not reported.
:::

```csharp
public sealed class Barista(IAwaitenResolver resolver)   // locates dependencies at run time
{
    public Cup Serve() => resolver.Resolve<Cup>();
}
```
