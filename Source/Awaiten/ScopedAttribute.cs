using System;

namespace Awaiten;

// S2326: the type parameter is the Awaiten source generator's input — it reads the implementation
// and service types from the attribute's type arguments via Roslyn symbols, so it is intentionally
// not referenced in the attribute body.
#pragma warning disable S2326

/// <summary>
///     Registers <typeparamref name="TImplementation" /> as scoped on the
///     <see cref="ContainerAttribute">container</see>. The service type is the implementation itself.
/// </summary>
/// <remarks>
///     Real scope semantics (a single instance per <c>CreateScope</c>) arrive in a later phase; for
///     now a scoped registration is resolved as a singleton.
/// </remarks>
/// <typeparam name="TImplementation">The concrete type to construct and resolve.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ScopedAttribute<TImplementation> : Attribute
	where TImplementation : class;

/// <summary>
///     Registers <typeparamref name="TImplementation" /> as scoped exposed through the service type
///     <typeparamref name="TService" />.
/// </summary>
/// <remarks>
///     Real scope semantics (a single instance per <c>CreateScope</c>) arrive in a later phase; for
///     now a scoped registration is resolved as a singleton.
/// </remarks>
/// <typeparam name="TImplementation">The concrete type to construct.</typeparam>
/// <typeparam name="TService">The service type under which the instance is resolved.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ScopedAttribute<TImplementation, TService> : Attribute
	where TImplementation : class, TService;

#pragma warning restore S2326
