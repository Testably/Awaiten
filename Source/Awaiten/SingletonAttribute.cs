using System;

namespace Awaiten;

// S2326: the type parameter is the Awaiten source generator's input — it reads the implementation
// and service types from the attribute's type arguments via Roslyn symbols, so it is intentionally
// not referenced in the attribute body.
#pragma warning disable S2326

/// <summary>
///     Registers <typeparamref name="TImplementation" /> as a singleton on the
///     <see cref="ContainerAttribute">container</see>: a single instance is constructed once and
///     cached for the lifetime of the container. The service type is the implementation itself.
/// </summary>
/// <typeparam name="TImplementation">The concrete type to construct and resolve.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class SingletonAttribute<TImplementation> : Attribute
	where TImplementation : class;

/// <summary>
///     Registers <typeparamref name="TImplementation" /> as a singleton exposed through the service
///     type <typeparamref name="TService" />: a single instance is constructed once and cached for the
///     lifetime of the container.
/// </summary>
/// <typeparam name="TImplementation">The concrete type to construct.</typeparam>
/// <typeparam name="TService">The service type under which the instance is resolved.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class SingletonAttribute<TImplementation, TService> : Attribute
	where TImplementation : class, TService;

#pragma warning restore S2326
