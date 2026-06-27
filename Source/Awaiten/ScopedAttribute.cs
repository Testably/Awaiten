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
///     A scoped registration resolves to a single instance per scope created via
///     <c>CreateScope</c>; the container itself acts as the root scope.
/// </remarks>
/// <typeparam name="TImplementation">The concrete type to construct and resolve.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ScopedAttribute<TImplementation> : Attribute
	where TImplementation : class
{
	/// <summary>
	///     The name of a method on the container that produces the instance instead of a constructor
	///     (an abstract factory). The method may be static or instance, must return a
	///     <typeparamref name="TImplementation" />, and its parameters are resolved from the graph; the
	///     result is cached once per scope, like any other scoped registration.
	/// </summary>
	public string? Factory { get; set; }
}

/// <summary>
///     Registers <typeparamref name="TImplementation" /> as scoped exposed through the service type
///     <typeparamref name="TService" />.
/// </summary>
/// <remarks>
///     A scoped registration resolves to a single instance per scope created via
///     <c>CreateScope</c>; the container itself acts as the root scope.
/// </remarks>
/// <typeparam name="TImplementation">The concrete type to construct.</typeparam>
/// <typeparam name="TService">The service type under which the instance is resolved.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ScopedAttribute<TImplementation, TService> : Attribute
	where TImplementation : class, TService;

#pragma warning restore S2326
