# Resolving services

Once a container is declared, you ask it for services. This page covers the resolve surface. Most of the time you inject dependencies through constructors and never call resolve yourself, but the entry points matter at the edges of your app.

## Resolve

`Resolve<T>()` returns the service or throws if it is not registered.

```csharp
using var shop = new CoffeeShop.Root();

Cup cup = shop.Resolve<Cup>();
```

There is a `Type`-based overload for when you do not know the type at compile time.

```csharp
object cup = shop.Resolve(typeof(Cup));
```

## TryResolve

`TryResolve<T>(out T?)` returns `false` instead of throwing when the service is not available.

```csharp
if (shop.TryResolve<Cup>(out Cup? cup))
{
    // use cup
}
```

For an async-tainted service in the strict default, `TryResolve` reports `false`, because that service has no synchronous path. See [Async initialization](../async-initialization).

## ResolveAsync

`ResolveAsync<T>(CancellationToken)` constructs the service, awaits its initialization, and returns it ready to use. This is the entry point for async services.

```csharp
EspressoMachine machine = await shop.ResolveAsync<EspressoMachine>(cancellationToken);
```

## Keyed resolution

Each of the three methods has an overload that takes the `Key` a registration was declared with. This is the imperative counterpart to `[FromKey]` at an injection site, and it selects the same registration.

```csharp
IMilk oat = shop.Resolve<IMilk>("Oat");

if (shop.TryResolve<IMilk>(MilkKind.Whole, out IMilk? whole))
{
    // use whole
}

IMilk soy = await shop.ResolveAsync<IMilk>("Soy", cancellationToken);
```

The key is an `object`, so a string, an `enum` constant, or a `typeof(...)` all work, matching the key the registration carries. A `null` key resolves the unkeyed registration, exactly like the keyless overload. An unknown key follows the same miss rules as above: `Resolve` throws, `TryResolve` reports `false`. See [Keyed services](../registration/keyed-services).

## Prefer constructor injection

You rarely call these methods deep in your code. You call them once at the composition root to pull out the top-level service, and everything below is wired by its constructors. A `Barista` that needs a `Grinder` just declares it:

```csharp
public sealed class Barista(Grinder grinder, EspressoMachine machine);
```

For the shapes a dependency can take, like `Func<T>` or collections, read on.

## Where to go next

- [Relationships](./relationships) for `Func`, `Lazy`, and `Task`.
- [Collections](./collections) for many implementations at once.
- [Property injection](./property-injection) for filling properties instead of constructor parameters.
