# Collections

When a service has several implementations, ask for a collection to get them all. Every syrup on the bar is an `ISyrup`. A consumer that wants the whole set asks for a collection of them.

## Resolve every implementation

Register the implementations, then depend on a collection shape.

```csharp
[Container]
[Singleton<VanillaSyrup, ISyrup>]
[Singleton<CaramelSyrup, ISyrup>]
[Singleton<HazelnutSyrup, ISyrup>]
public static partial class CoffeeShop;

public sealed class SyrupBar(IEnumerable<ISyrup> syrups);
```

## Supported shapes

Any of these constructor parameter types works, and each is resolvable by type as well.

```csharp
IEnumerable<ISyrup>
ISyrup[]
IReadOnlyList<ISyrup>
IReadOnlyCollection<ISyrup>
IList<ISyrup>
ICollection<ISyrup>
```

Members come back in registration order. Each keeps its own lifetime. A service type with no registrations resolves to an empty collection, not an error.

## Async collections

If a member is async-initialized, you have two shapes.

`IAsyncEnumerable<T>` streams the members and awaits each one's initialization as you go.

```csharp
public sealed class Warmer(IAsyncEnumerable<IDrink> drinks);
```

An awaited collection like `Task<IReadOnlyList<T>>` materializes and awaits every async member, but launders the taint so a synchronous consumer can still hold it.

```csharp
Task<IReadOnlyList<IDrink>> drinks = shop.Resolve<Task<IReadOnlyList<IDrink>>>();
```

## Registering your own collection

Register a collection shape explicitly and it wins. Awaiten stops synthesizing the collection for that element type and uses yours.

```csharp
[Singleton<SyrupBundle, IEnumerable<ISyrup>>]
```

## Where to go next

- [Keyed dictionaries](./keyed-dictionaries) to get implementations keyed by name.
- [Composites](../registration/composites) to expose many implementations as one service.
