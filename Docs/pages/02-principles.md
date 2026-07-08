---
title: Principles & the composition root
sidebar_position: 2
---

# Principles & the composition root

The rest of these docs teach the *mechanics*: how to register services, resolve them, and keep their lifetimes honest. This page is about the *design* those mechanics serve. Awaiten enforces the mechanics for you (lifetimes, cycles, async ordering) and turns the ones it can see into build errors. It cannot enforce the design. That part is on you, and it is what this page is for.

## What DI is really for

Dependency injection is a means to an end, and the end is loose coupling. You depend on abstractions you can substitute, so you can test a class in isolation, swap an implementation without touching its callers, and reason about one piece at a time. The container is not the point. It is just the machine that assembles the graph so you do not hand-wire it.

A container that produces tightly coupled code is a container that has not helped you. Awaiten makes the wiring cheap and the lifetimes safe, but a clean build is not a clean design. You can follow every page here, get zero diagnostics, and still write code that is impossible to test in isolation. The habits below are what keep that from happening.

## The composition root

Your `[Container]` is the **composition root**: the one place that knows how the whole graph fits together. The generated `Root` sits at your application's entry point, composes everything once, and owns it until shutdown.

```csharp
[Container]
[Singleton<EspressoMachine>]
[Scoped<Order>]
[Transient<Cup>]
public static partial class CoffeeShop;

// at the entry point, and nowhere else:
await using var shop = new CoffeeShop.Root();
```

Compose the graph *only* there. The opposite is composition scattered through the code: a service reaching for the container to build its own collaborators, a factory buried three layers deep newing up a database connection. When wiring is spread out like that, no single place tells you what the application is made of, and every one of those sites is a small coupling to how things are built.

One rule follows from this, and it is the most important rule on the page:

**Domain and application code must not reference Awaiten.** The container may know about Awaiten, because the container *is* the composition root. Everything it composes should be plain C# that has never heard of a container. See [Keeping DI out of your domain](#keeping-di-out-of-your-domain) below for how Awaiten helps you hold that line.

## Volatile vs. stable dependencies

Not everything deserves an abstraction and a registration. Deciding what does is the judgment the container cannot make for you.

A dependency is **volatile** when it does something you cannot control from a test or reproduce on demand: it talks to the outside world, has side effects, or gives different answers at different times. The clock, randomness, the filesystem, a database, an HTTP call, configuration read at startup. In the coffee shop, the espresso machine and the payment terminal are volatile: one touches hardware, the other calls the bank. These are exactly the things you want behind an abstraction so a test can stand in a fake, and exactly the things that belong in the container.

A dependency is **stable** when it is deterministic and self-contained: a value type, a pure helper, a piece of data with no I/O behind it. A coffee `Recipe` that holds a list of steps is stable. Abstracting it buys you nothing, and registering it just adds noise to the root.

:::tip[The decision rule]
If a dependency reaches outside the process, keeps state that outlives one call, or answers differently from one moment to the next, put it behind an abstraction and register it. If it is a deterministic value or a pure function, just `new` it up where you need it. Register the volatile, construct the stable.
:::

## The four anti-patterns

Loose coupling has four classic failure modes. The names and the taxonomy come from Steven van Deursen and Mark Seemann's [*Dependency Injection Principles, Practices, and Patterns*](https://www.manning.com/books/dependency-injection-principles-practices-patterns) (Manning, 2019), the standard reference on DI in .NET; Seemann also writes about them on [his blog](https://blog.ploeh.dk/).

### Control Freak

A class takes control of building its own volatile dependency instead of receiving it, by newing it up or reaching a static accessor.

```csharp
public sealed class Barista
{
    public Receipt Serve(Order order)
    {
        var terminal = new PaymentTerminal();   // built here, cannot be substituted
        var when = DateTime.UtcNow;             // and the clock is reached statically
        // ...
    }
}
```

Now `Barista` cannot be tested without charging a real card and reading the wall clock, and no caller can swap either one.

**In Awaiten:** the tool does not stop a `new` inside a method, and it cannot. The fix is not a diagnostic; it is design. Depend on an abstraction (`IPaymentGateway`, `ITimeSystem`), take it through the constructor, and register it on the container.

### Service Locator

A class takes the container itself and asks it for dependencies at run time.

```csharp
public sealed class Barista(IAwaitenResolver resolver)
{
    public Receipt Serve(Order order)
    {
        var gateway = resolver.Resolve<IPaymentGateway>();   // real dependency, hidden
        // ...
    }
}
```

This looks like DI, but it is the opposite — Seemann's [*Service Locator is an Anti-Pattern*](https://blog.ploeh.dk/2010/02/03/ServiceLocatorisanAnti-Pattern/) is the classic write-up. `Barista`'s real dependencies no longer show in its constructor, so you cannot tell what it needs without reading its body, and Awaiten cannot check the graph it hides.

**Rule: never inject `IAwaitenResolver`, `IAwaitenScope`, or `IAwaitenRoot` into a service.** They are seams for the composition root, not for the classes it composes. Awaiten flags this one for you: holding a resolver in anything but the `[Container]` is the suppressible warning [AWT135](./diagnostics#awt135). Take the dependency you actually need through the constructor and let the container supply it.

### Ambient Context

A class reaches a dependency through static, process-wide state rather than receiving it.

```csharp
var when = DateTime.UtcNow;             // the ambient clock
var barista = Thread.CurrentPrincipal;  // the ambient identity
Log.Info("order served");               // the ambient logger
```

It reads conveniently, but the dependency is invisible in the signature and shared across the whole process, so a test cannot pin the time, the identity, or capture the log without global setup.

**In Awaiten:** not something the tool can see. Inject the abstraction instead. Give the barista an `ITimeSystem` and read `timeSystem.DateTime.UtcNow`, the same clock the rest of the coffee shop shares through the container, so a test can freeze it.

### Constrained Construction

A design that forces a particular constructor shape or late-binds types by reflection, so a class can only be built if it satisfies an unwritten contract discovered at run time.

**In Awaiten:** avoided structurally, so there is nothing for you to get wrong. Awaiten generates the construction code at compile time from the constructor you actually wrote. There is no reflection, no required signature, and no run-time discovery to satisfy. If a wiring cannot be built, you learn it as a build error, not as a surprise at startup.

## Keeping DI out of your domain

Plain constructor injection needs no attribute at all. A class that asks for its collaborators through its constructor is already a clean POCO, and that is the common path.

```csharp
public sealed class Barista(ITimeSystem timeSystem, IPaymentGateway gateway);   // no attributes, no Awaiten reference
```

The few features that would otherwise put an Awaiten attribute on a domain class each have a **container-side form** that keeps the class plain, because the instruction lives on the composition root where Awaiten belongs:

| Need | Container-side idiom (clean domain) | Consumer-side escape hatch |
|---|---|---|
| A host-owned service | [`[ImportService<T>]`](./advanced/external-services) | *(none needed)* |
| A keyed implementation | [`WhenInjectedInto`](./registration/keyed-services) or a factory [`[FromKey]`](./registration/keyed-services) | `[FromKey]` on the parameter |
| A runtime argument | a factory `[Arg]` behind [`Func<TArg, T>`](./resolution/runtime-arguments) | `[Arg]` on the parameter |
| Property injection | [`[InjectProperty<T>]`](./resolution/property-injection) | `[Inject]` on the property |

Prefer the container-side form every time. It leaves the domain class free of any reference to Awaiten, which is the whole point. The three consumer-side attributes (`[Arg]`, `[FromKey]`, `[Inject]`) are honest escape hatches for the rare shape the root cannot express, not defects, but each one couples the class that carries it to Awaiten.

Awaiten guards part of this for you: applying a composition attribute (`[Singleton]`, `[Scan]`, and the like) in an assembly that declares no `[Container]` is the suppressible warning [AWT134](./diagnostics#awt134). That guard is best-effort, and it goes quiet in a single-project app that mixes the root with its domain. To enforce the full boundary, add an **architecture test** that asserts your domain and application assemblies carry no reference to Awaiten. That turns a convention into a red build the day someone crosses the line.

[aweXpect.Reflection](https://docs.testably.org/Extensions/aweXpect.Reflection/) expresses that rule directly. Point it at the assembly that holds your domain code and assert it depends on nothing in the `Awaiten` namespace:

```csharp
using aweXpect;
using aweXpect.Reflection;

[Fact]
public async Task Domain_has_no_reference_to_Awaiten()
{
    // every type in the assembly that holds your domain and application code
    var domain = In.AssemblyContaining<Barista>().Types();

    await Expect.That(domain)
        .DoNotDependOn(Types.InNamespace("Awaiten"));
}
```

Only the composition-root assembly — the one that declares your `[Container]` — should turn up a reference to Awaiten. The day a `[Singleton]` or an `[Inject]` sneaks into `Barista`'s assembly, this test goes red, whether or not AWT134 could see it.

## When power becomes a smell

Some of Awaiten's features resolve ambiguity gracefully: [keyed services](./registration/keyed-services), contextual binding, [scanning](./registration/scanning), and [context-aware factories](./resolution/context-aware-factories) all let you say "it depends" and have the container sort it out. That power is real, and sometimes exactly right. But resolving an ambiguity is not the same as removing it, and a container that smooths one over can hide a design that wants to be split into two clear abstractions instead of one clever registration.

Each of those feature pages carries a **"When not to reach for this"** note that names the simpler thing to prefer and the genuine case that justifies the feature. Read them as a set. When you find yourself reaching for the powerful form to paper over a design you are not happy with, that is the smell, and the fix usually lives in the design, not the registration.

## Where to go next

- [Getting started](./getting-started) if you have not built a container yet.
- [Keyed services](./registration/keyed-services) and [scanning](./registration/scanning) for the registration features to use sparingly.
- [Property injection](./resolution/property-injection) and [context-aware factories](./resolution/context-aware-factories) for the resolution features to use sparingly.
- [Diagnostics](./diagnostics) for the mechanics Awaiten *does* enforce.
