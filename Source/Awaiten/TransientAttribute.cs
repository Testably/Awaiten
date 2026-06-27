using System;

namespace Awaiten;

// S2326: the type parameter is the Awaiten source generator's input — it reads the implementation
// and service types from the attribute's type arguments via Roslyn symbols, so it is intentionally
// not referenced in the attribute body.
#pragma warning disable S2326

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
}

/// <summary>
///     Registers <typeparamref name="TImplementation" /> as transient exposed through the service
///     type <typeparamref name="TService" />: a new instance is constructed on every request.
/// </summary>
/// <typeparam name="TImplementation">The concrete type to construct.</typeparam>
/// <typeparam name="TService">The service type under which an instance is resolved.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class TransientAttribute<TImplementation, TService> : Attribute
	where TImplementation : class, TService;

#pragma warning restore S2326
