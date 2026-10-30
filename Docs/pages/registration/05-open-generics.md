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

Each type argument gets its own closed implementation, and each keeps the declared lifetime. Awaiten works out which closed forms you need at compile time from what the graph actually asks for: constructor and factory parameters, [injected properties](../resolution/property-injection) and the parameters of [lifecycle hooks](../lifetime/lifecycle-hooks), also when the closed service sits inside a collection, a `Lazy<T>` or a `Func<T>`.

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

## Decorate or compose the closings

Decorators and composites take the same `typeof` form, so one behavior wraps every closing of an open generic service. This is the pipeline-behavior pattern:

```csharp
[Transient(typeof(Handler<>), typeof(IHandler<>))]
[Decorate(typeof(LoggingBehavior<>), typeof(IHandler<>))]
[Composite(typeof(CompositeHandler<>), typeof(IHandler<>))]
```

See [Decorators](./decorators#decorate-every-closing-of-an-open-generic) and [Composites](./composites#compose-every-closing-of-an-open-generic).

*Note: the implementation and service must have the same arity ([AWT125](../diagnostics#awt125)), and a closed type argument must satisfy the implementation's constraints ([AWT126](../diagnostics#awt126)).*

## Registration options

The `typeof` form takes the same options as the generic form. On an open generic each option applies to every closing.

| Option | Open generic | Closed `typeof` |
|---|---|---|
| `Key` | yes, every closing is keyed | yes |
| `Fallback` | yes, per closing | yes |
| `WhenInjectedInto` | yes | yes |
| `SuppressDisposal` | yes | yes |
| `OnActivated` / `OnRelease` | yes, the hook may be generic | yes |
| `Factory` | a generic method, closed per closing | yes |
| `Instance` (`[Singleton]` only) | no ([AWT207](../diagnostics#awt207)) | yes |
| `Eager` (`[Singleton]` only) | no ([AWT207](../diagnostics#awt207)) | yes |

A closed `typeof` registration, such as `[Singleton(typeof(Clock), typeof(IClock))]`, is the same registration as `[Singleton<Clock, IClock>]`. The generic form checks at compile time that the implementation is a class that implements the service; the `typeof` form gets the same check from Awaiten ([AWT206](../diagnostics#awt206)).

`Instance` and `Eager` describe one concrete instance. An open generic only becomes concrete per closing, and which closings exist depends on what the rest of the graph asks for, so both need a closed type. Register the closings you want eager, or pre-built, explicitly.

### Generic factories

`Factory` on an open generic names a generic method. Awaiten closes it for each closing, just like the implementation:

```csharp
[Container]
[Scoped(typeof(IRepository<>), Factory = nameof(CreateRepository))]
public static partial class CoffeeShop
{
    private static IRepository<T> CreateRepository<T>(DbConnection connection)
        => new Repository<T>(connection);
}
```

A request for `IRepository<Order>` calls `CreateRepository<Order>(connection)`. The rules:

- **Type parameters.** The method has exactly the service's type parameters, mapped in declaration order, like open decorators and composites. A method with a different number is an error ([AWT208](../diagnostics#awt208)).
- **Constraints.** A closing that violates the method's constraints is skipped with an error ([AWT209](../diagnostics#awt209)), like a closing that violates the implementation's own ([AWT126](../diagnostics#awt126)).
- **Return type.** The method returns the closed service, or `Task<T>`/`ValueTask<T>` of it. An async factory is awaited and makes the closing async, like a non-generic async factory.
- **Parameters.** They follow the rules of a non-generic factory: graph dependencies (including other open generics closed the same way), `CancellationToken`, `[FromKey]`, `[RequestingType]`, and `[Arg]` on a transient reached through `Func<…, T>`.
- **Overloads.** An overloaded factory is ambiguous ([AWT112](../diagnostics#awt112)), as for a non-generic one.

### Lifecycle hooks

`OnActivated` and `OnRelease` run for every closing. The hook can be generic over the service's type arguments, which it receives like a hook on an [open-generic scan](./scanning#generic-hooks-on-an-open-marker): Awaiten binds it from the closed service, so `IConnection<Order>` calls `Opened<Order>(…)`. A non-generic hook, with its first parameter typed as `object` or a shared base, works for every closing too.

```csharp
[Container]
[Scoped(typeof(Connection<>), typeof(IConnection<>), OnActivated = nameof(Opened))]
public static partial class CoffeeShop
{
    private static void Opened<T>(IConnection<T> connection, ILogger logger)
        => logger.Log($"Opened a connection for {typeof(T).Name}");
}
```

### Overridable defaults

`Fallback` makes an open generic an overridable default, typically in a [module](./modules#overridable-defaults). It applies per closing: a closed registration replaces the default for its own closing only, and a strong open registration of the same service replaces it for every closing.

```csharp
[Container]
[Singleton(typeof(MemoryCache<>), typeof(ICache<>), Fallback = Fallback.Warn)]
[Singleton<OrderCache, ICache<Order>>]   // ICache<Order> is OrderCache, every other closing MemoryCache<T>
public static partial class CoffeeShop;
```

## Where to go next

- [Collections](../resolution/collections) for resolving every implementation.
- [Generic variance](../resolution/generic-variance) for covariant and contravariant matching.
