# Open generics

A generic service like `IRepository<T>` usually has one generic implementation like `Repository<T>`. You do not want to register it once per entity type. Register the open generic once, and Awaiten closes it for each type your app actually uses.

## Register the open type

Use the `typeof` form with an unbound generic type.

```csharp
[Container]
[Transient(typeof(Repository<>), typeof(IRepository<>))]
public static partial class CoffeeShop;
```

## Resolve a closed type

Ask for any closed form and Awaiten builds the matching implementation.

```csharp
IRepository<Order> orders = shop.Resolve<IRepository<Order>>();
IRepository<Customer> customers = shop.Resolve<IRepository<Customer>>();
```

Each type argument gets its own closed implementation, and each keeps the declared lifetime. Awaiten works out which closed forms you need at compile time from what the graph actually asks for.

## Self-registration

You can register an open generic under itself, without a separate service type.

```csharp
[Transient(typeof(Handler<>))]
// resolve: shop.Resolve<Handler<OrderPlaced>>()
```

## Collections and keys

Open registrations join collections. A request for `IEnumerable<IHandler<OrderPlaced>>` unions every open registration closed at `OrderPlaced`, alongside any explicit closed registrations. A `Key` on the open registration flows to every closed implementation, so you select with `[FromKey]` as usual.

```csharp
[Transient(typeof(FastRepository<>), typeof(IRepository<>), Key = "fast")]
```

*Note: the implementation and service must have the same arity ([AWT125](../diagnostics#awt125)), and a closed type argument must satisfy the implementation's constraints ([AWT126](../diagnostics#awt126)).*

## Where to go next

- [Collections](../resolution/collections) for resolving every implementation.
- [Generic variance](../advanced/generic-variance) for covariant and contravariant matching.
