---
description: Give a factory the type of the consumer it builds for with a [RequestingType] parameter.
---

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

:::note
The parameter must be `System.Type` ([AWT162](../09-diagnostics.md#awt162)), and a factory cannot mix `[RequestingType]` with an `[Arg]` runtime argument ([AWT163](../09-diagnostics.md#awt163)).
:::

:::tip[When not to reach for this]
The requesting-type hook is advanced, and it makes one abstraction mean different things to different consumers, which is exactly the kind of implicit coupling DI is meant to remove. Reserve it for a genuinely cross-cutting service where the consumer's identity is the whole point, like a logger named after the class that uses it. For anything else, give each consumer the dependency it actually needs, see [Design principles](../08-design-principles.md#when-power-becomes-a-smell).
:::

## Where to go next

- [Factories and instances](../04-registration/01-factories-and-instances.md) for factories in general.
- [Property injection](./05-property-injection.md) for injecting a context-aware logger into a property.
