# Context-aware factories

Now and then a service should know who asked for it. The classic case is a logger named after the class that uses it. `[RequestingType]` gives a factory the consumer's type at each construction site.

## Ask for the requesting type

Add a `[RequestingType] Type` parameter to a factory method. Awaiten fills it with the type whose constructor or property is being satisfied.

```csharp
[Container]
[Singleton<Prefix>]
[Transient<ILogger>(Factory = nameof(CreateLogger))]
[Transient<Barista>]
public static partial class CoffeeShop
{
    private static Logger CreateLogger([RequestingType] Type? requestingType, Prefix prefix)
        => new Logger(requestingType?.FullName ?? "<root>", prefix);
}
```

Now a `Barista` that needs an `ILogger` gets one tagged with `Barista`, and a `Register` gets one tagged with `Register`. Each consumer gets its own.

## Resolved directly? The type is null

When you resolve the service straight from the container, there is no consumer. The parameter is `null`, so declare it as `Type?` and handle that case.

```csharp
ILogger logger = shop.Resolve<ILogger>();   // requestingType is null here
```

## Works with the rest

The other factory parameters resolve from the graph as usual. `[RequestingType]` flows through `Func<T>` and through async factories too.

*Note: the parameter must be `System.Type` ([AWT162](../diagnostics#awt162)), and a factory cannot mix `[RequestingType]` with an `[Arg]` runtime argument ([AWT163](../diagnostics#awt163)).*

## Where to go next

- [Factories and instances](../registration/factories-and-instances) for factories in general.
- [Property injection](./property-injection) for injecting a context-aware logger into a property.
