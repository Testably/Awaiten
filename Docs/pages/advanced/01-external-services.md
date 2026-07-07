# External services

Not everything comes from Awaiten. When you run under ASP.NET Core or the generic host, some services live in the host's provider. The payment gateway, the framework logger, and the configuration are wired there. External services let an Awaiten graph pull those in.

## Pull a single type with `[ImportService<T>]`

Declare a service type external with `[ImportService<T>]` on the container. Every unregistered dependency of that type - keyed or not, whether a constructor parameter, a factory parameter, or an `[Inject]` property - is satisfied from the external provider instead of the graph. No Awaiten registration is needed for it, and every *other* unresolved dependency still gets the missing-dependency check (AWT101), so this is safer than the blanket fall-through below.

```csharp
[Container]
[ImportService<IPaymentGateway>]
[Singleton<Register>]
public static partial class CoffeeShop;

public sealed class Register(Till till, IPaymentGateway gateway);   // IPaymentGateway comes from the host
```

Combine it with `[FromKey]` for a keyed external service; the key is forwarded to the resolver.

```csharp
[Container]
[ImportService<IClock>]
[Singleton<Report>]
public static partial class CoffeeShop;

public sealed class Report([FromKey("utc")] IClock clock);
```

`[ImportService<T>]` works on an imported `[Module]` too, exactly as it does on the container.

Only the *direct* dependency of type `T` is routed. A relationship or collection over it - `Func<T>`, `Lazy<T>`, `Task<T>`, `IEnumerable<T>`, and the like - is not, because the resolver hands back an instance, not a deferred or fanned-out shape. Such a dependency still resolves from the Awaiten graph, so with no registration it surfaces as AWT101 (or an empty collection). Declaring a type external that nothing in the graph consumes is a dead declaration and is reported as AWT176.

## Fall through everything with `[ImportServices]`

Put `[ImportServices]` on the container and any unregistered direct dependency falls through to the external provider automatically. You trade some compile-time safety for less annotation.

```csharp
[Container]
[ImportServices]
[Singleton<Register>]
public static partial class CoffeeShop;

// Register's IPaymentGateway is resolved from the host, unannotated
```

## Providing the external resolver

Awaiten does not create the external provider. It asks an *external resolver* for these dependencies, and you supply that resolver once, up front.

Under the [MS.DI bridge](../msdi-bridge) this is automatic. `AddGeneratedContainer` and the startup initialization wire the container to the host's `IServiceProvider`, so `[ImportService<T>]` and `[ImportServices]` dependencies resolve from the host with nothing extra to do.

Standing alone, you set it yourself. Implement `IExternalResolver` and assign it through `IExternalResolverHost` before the first resolve.

```csharp
public sealed class HostResolver : IExternalResolver
{
    public bool TryResolve(Type serviceType, object? key, out object? instance)
    {
        if (serviceType == typeof(IPaymentGateway))
        {
            instance = new StripeGateway();
            return true;
        }

        instance = null;
        return false;   // not found: Awaiten reports a missing external dependency
    }
}

using var shop = new CoffeeShop.Root();
((IExternalResolverHost)shop).ExternalResolver = new HostResolver();

var register = shop.Resolve<Register>();   // its IPaymentGateway comes from HostResolver
```

The `key` argument carries the `[FromKey]` value for a keyed external dependency. A child scope, and the throwaway scope behind an `Owned<T>`, inherit the resolver from the scope that created them, so setting it once on the root is enough.

## Verifying the wiring

Awaiten advertises its external dependencies so you can check them at startup. If one is missing from the provider, verification throws and names it. See [MS.DI bridge](../msdi-bridge) for `VerifyAwaitenContainers`.

## Ownership

An externally supplied instance belongs to the external provider. Awaiten never disposes it.

## Where to go next

- [MS.DI bridge](../msdi-bridge) for host integration and verification.
- [Keyed services](../registration/keyed-services) for `[FromKey]`.
