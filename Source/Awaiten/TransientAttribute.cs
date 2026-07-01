using System;

namespace Awaiten;

// S2326: the type parameter is the Awaiten source generator's input — it reads the implementation
// and service types from the attribute's type arguments via Roslyn symbols, so it is intentionally
// not referenced in the attribute body.
#pragma warning disable S2326

/// <summary>
///     Registers an <b>open generic</b> implementation as transient, using <see cref="Type" />
///     arguments because an unbound generic (<c>typeof(Repository&lt;&gt;)</c>) cannot be a type
///     argument. Resolving a closed service (<c>IRepository&lt;Order&gt;</c>) constructs a fresh
///     matching closed implementation (<c>Repository&lt;Order&gt;</c>) on every request.
/// </summary>
/// <example><c>[Transient(typeof(Repository&lt;&gt;), typeof(IRepository&lt;&gt;))]</c></example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class TransientAttribute : Attribute
{
	/// <summary>
	///     Registers the open generic <paramref name="implementation" /> as itself.
	/// </summary>
	/// <param name="implementation">The open generic concrete type, e.g. <c>typeof(Repository&lt;&gt;)</c>.</param>
	public TransientAttribute(Type implementation)
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
	public TransientAttribute(Type implementation, Type service)
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
///     Registers <typeparamref name="TImplementation" /> as transient on the
///     <see cref="ContainerAttribute">container</see>: a new instance is constructed on every
///     request. The service type is the implementation itself.
/// </summary>
/// <typeparam name="TImplementation">The concrete type to construct and resolve.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class TransientAttribute<TImplementation> : Attribute
	where TImplementation : class
{
	/// <summary>
	///     The name of a method on the container that produces the instance instead of a constructor
	///     (an abstract factory). The method may be static or instance, must return a
	///     <typeparamref name="TImplementation" />, and its parameters are resolved from the graph; it is
	///     invoked anew on every request, like any other transient registration.
	/// </summary>
	public string? Factory { get; set; }

	/// <summary>
	///     An optional resolution key. Several implementations may share one service type under
	///     different keys; consumers select one with <c>[FromKey]</c>.
	/// </summary>
	public string? Key { get; set; }
}

/// <summary>
///     Registers <typeparamref name="TImplementation" /> as transient exposed through the service
///     type <typeparamref name="TService" />: a new instance is constructed on every request.
/// </summary>
/// <typeparam name="TImplementation">The concrete type to construct.</typeparam>
/// <typeparam name="TService">The service type under which an instance is resolved.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class TransientAttribute<TImplementation, TService> : Attribute
	where TImplementation : class, TService
{
	/// <summary>
	///     The name of a method on the container that produces the instance instead of a constructor
	///     (an abstract factory). The method may be static or instance, must return a
	///     <typeparamref name="TImplementation" />, and its parameters are resolved from the graph; it is
	///     invoked anew on every request, like any other transient registration. Registering the same
	///     <typeparamref name="TImplementation" /> under several service types with the same factory uses
	///     the one factory for all of them.
	/// </summary>
	public string? Factory { get; set; }

	/// <summary>
	///     An optional resolution key. Several implementations may share one service type under
	///     different keys; consumers select one with <c>[FromKey]</c>.
	/// </summary>
	public string? Key { get; set; }
}

#pragma warning restore S2326
