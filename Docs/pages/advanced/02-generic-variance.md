# Generic variance

C# generic interfaces can be covariant (`out T`) or contravariant (`in T`). Awaiten honors that when it resolves. A handler registered for a base event type can satisfy a request for a derived one, the same way the compiler would allow the assignment.

## Contravariant handlers

A handler declared with `in T` accepts more derived types. Register one for `DomainEvent` and it satisfies a request for `IHandler<OrderPlaced>`.

```csharp
public interface IHandler<in T>;
public sealed class AuditHandler : IHandler<DomainEvent>;

[Container]
[Transient<AuditHandler, IHandler<DomainEvent>>]
[Transient<OrderConsumer>]      // needs IHandler<OrderPlaced>
public static partial class CoffeeShop;

IHandler<OrderPlaced> handler = shop.Resolve<IHandler<OrderPlaced>>();
```

## Covariant factories

A factory declared with `out T` produces more derived types. Register one for `OrderPlaced` and it satisfies a request for `IFactory<DomainEvent>`.

```csharp
public interface IFactory<out T> where T : DomainEvent;
```

## The rules

- An exact registration always wins over a variance match.
- The nearest closure wins, mirroring how the compiler resolves overloads.
- Invariant interfaces never match a different closure.
- Keyed registrations are never variance candidates.

Variance applies to single resolution, to collections (exact matches lead, variance matches are unioned in), to awaited collections, and to `Func<T>` relationships.

## Where to go next

- [Open generics](../registration/open-generics) for generic implementations closed on demand.
- [Collections](../resolution/collections) for resolving every match.
