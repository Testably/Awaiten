using System;

namespace Awaiten;

// S2326: the type parameter is the Awaiten source generator's input — it reads the implementation
// and service types from the attribute's type arguments via Roslyn symbols, so it is intentionally
// not referenced in the attribute body.
#pragma warning disable S2326

/// <summary>
///     Registers an <b>open generic</b> implementation as scoped, using <see cref="Type" /> arguments
///     because an unbound generic (<c>typeof(Repository&lt;&gt;)</c>) cannot be a type argument.
///     Resolving a closed service (<c>IRepository&lt;Order&gt;</c>) constructs the matching closed
///     implementation (<c>Repository&lt;Order&gt;</c>) once per scope for each closed type argument.
/// </summary>
/// <example><c>[Scoped(typeof(Repository&lt;&gt;), typeof(IRepository&lt;&gt;))]</c></example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ScopedAttribute : Attribute
{
	/// <summary>
	///     Registers the open generic <paramref name="implementation" /> as itself.
	/// </summary>
	/// <param name="implementation">The open generic concrete type, e.g. <c>typeof(Repository&lt;&gt;)</c>.</param>
	public ScopedAttribute(Type implementation)
	{
		Implementation = implementation;
		Service = implementation;
	}

	/// <summary>
	///     Registers the open generic <paramref name="implementation" /> exposed through the open
	///     generic <paramref name="service" />.
	/// </summary>
	/// <param name="implementation">The open generic concrete type, e.g. <c>typeof(Repository&lt;&gt;)</c>.</param>
	/// <param name="service">The open generic service type, e.g. <c>typeof(IRepository&lt;&gt;)</c>.</param>
	public ScopedAttribute(Type implementation, Type service)
	{
		Implementation = implementation;
		Service = service;
	}

	/// <summary>The open generic concrete type to construct.</summary>
	public Type Implementation { get; }

	/// <summary>The open generic service type under which closed instances are resolved.</summary>
	public Type Service { get; }

	/// <summary>
	///     An optional resolution key. Several open generic implementations may share one service type
	///     under different keys; consumers select one with <c>[FromKey]</c>. The key flows onto every
	///     closed implementation expanded from this registration.
	/// </summary>
	public string? Key { get; set; }
}

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

	/// <summary>
	///     An optional resolution key. Several implementations may share one service type under
	///     different keys; consumers select one with <c>[FromKey]</c>.
	/// </summary>
	public string? Key { get; set; }
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
	where TImplementation : class, TService
{
	/// <summary>
	///     The name of a method on the container that produces the instance instead of a constructor
	///     (an abstract factory). The method may be static or instance, must return a
	///     <typeparamref name="TImplementation" />, and its parameters are resolved from the graph; the
	///     result is cached once per scope, like any other scoped registration. Registering the same
	///     <typeparamref name="TImplementation" /> under several service types with the same factory
	///     shares a single instance.
	/// </summary>
	public string? Factory { get; set; }

	/// <summary>
	///     An optional resolution key. Several implementations may share one service type under
	///     different keys; consumers select one with <c>[FromKey]</c>.
	/// </summary>
	public string? Key { get; set; }
}

#pragma warning restore S2326
