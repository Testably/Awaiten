using System;

namespace Awaiten;

// S2326: the type parameter is the source generator's input. It reads the implementation and service
// types from the attribute's type arguments via Roslyn, so the body never references it.
#pragma warning disable S2326

/// <summary>
///     Registers an open generic implementation as a singleton. Resolving a closed service such as
///     <c>IRepository&lt;Order&gt;</c> constructs the matching <c>Repository&lt;Order&gt;</c> once and
///     caches one instance per closed type argument for the container's lifetime. Uses <see cref="Type" />
///     arguments because an unbound generic like <c>typeof(Repository&lt;&gt;)</c> cannot be a type argument.
/// </summary>
/// <example><c>[Singleton(typeof(Repository&lt;&gt;), typeof(IRepository&lt;&gt;))]</c></example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class SingletonAttribute : Attribute
{
	/// <summary>Registers the open generic <paramref name="implementation" /> as itself.</summary>
	/// <param name="implementation">The open generic concrete type, e.g. <c>typeof(Repository&lt;&gt;)</c>.</param>
	public SingletonAttribute(Type implementation)
	{
		Implementation = implementation;
		Service = implementation;
	}

	/// <summary>Registers the open generic <paramref name="implementation" /> under the open generic <paramref name="service" />.</summary>
	/// <param name="implementation">The open generic concrete type, e.g. <c>typeof(Repository&lt;&gt;)</c>.</param>
	/// <param name="service">The open generic service type, e.g. <c>typeof(IRepository&lt;&gt;)</c>.</param>
	public SingletonAttribute(Type implementation, Type service)
	{
		Implementation = implementation;
		Service = service;
	}

	/// <summary>The open generic concrete type to construct.</summary>
	public Type Implementation { get; }

	/// <summary>The open generic service type under which closed instances are resolved.</summary>
	public Type Service { get; }

	/// <summary>
	///     Optional resolution key. Several implementations can share a service type under different keys.
	///     The key flows onto every closed implementation expanded from this registration.
	/// </summary>
	public string? Key { get; set; }
}

/// <summary>
///     Registers <typeparamref name="TImplementation" /> as a singleton on the
///     <see cref="ContainerAttribute">container</see>. One instance is constructed and cached for the
///     container's lifetime. The service type is the implementation itself.
/// </summary>
/// <typeparam name="TImplementation">The concrete type to construct and resolve.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class SingletonAttribute<TImplementation> : Attribute
	where TImplementation : class
{
	/// <summary>
	///     Name of a container method that produces the instance instead of a constructor. It may be
	///     static or instance, must return <typeparamref name="TImplementation" />, and has its
	///     parameters resolved from the graph.
	/// </summary>
	public string? Factory { get; set; }

	/// <summary>
	///     Name of a container field or property holding a pre-built instance to expose. The container
	///     neither constructs nor disposes it. The caller owns it.
	/// </summary>
	public string? Instance { get; set; }

	/// <summary>
	///     Optional resolution key. Several implementations can share a service type under different keys.
	///     Consumers select one with <c>[FromKey]</c>.
	/// </summary>
	public string? Key { get; set; }

	/// <summary>
	///     Marks this as an overridable default, usually declared in a module. It applies only when
	///     nothing else registers the same service, so a container or another module can replace it.
	/// </summary>
	public bool Default { get; set; }

	/// <summary>
	///     Adds this registration only if the service is not already registered. Like <see cref="Default" />,
	///     for contributing a service from a module without overriding an existing one.
	/// </summary>
	public bool TryAdd { get; set; }

	/// <summary>
	///     Constructs the singleton at container build time instead of lazily on first resolve. This is
	///     the synchronous analog of <c>InitializeAsync</c>, which warms only async-initialized singletons.
	///     Eager singletons are built in registration order and disposed with the container like any other.
	/// </summary>
	public bool Eager { get; set; }

	/// <summary>
	///     The name of a <c>static void</c> method on the container accepting a
	///     <typeparamref name="TImplementation" />, invoked once the instance has been constructed - a
	///     synchronous post-construction initialization hook.
	/// </summary>
	public string? OnActivated { get; set; }

	/// <summary>
	///     The name of a <c>static void</c> method on the container accepting a
	///     <typeparamref name="TImplementation" />, invoked when the owning container or scope is disposed -
	///     in reverse creation order and before the instance's own disposal (it runs in addition to, not
	///     instead of, that disposal).
	/// </summary>
	public string? OnRelease { get; set; }
}

/// <summary>
///     Registers <typeparamref name="TImplementation" /> as a singleton exposed through
///     <typeparamref name="TService" />. One instance is constructed and cached for the container's lifetime.
/// </summary>
/// <typeparam name="TImplementation">The concrete type to construct.</typeparam>
/// <typeparam name="TService">The service type under which the instance is resolved.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class SingletonAttribute<TImplementation, TService> : Attribute
	where TImplementation : class, TService
{
	/// <summary>
	///     Name of a container method that produces the instance instead of a constructor. It may be
	///     static or instance, must return <typeparamref name="TImplementation" />, and has its
	///     parameters resolved from the graph. Registering the same implementation under several service
	///     types with the same factory shares one instance across them.
	/// </summary>
	public string? Factory { get; set; }

	/// <summary>
	///     Name of a container field or property of type <typeparamref name="TImplementation" /> holding a
	///     pre-built instance to expose. The container neither constructs nor disposes it. Registering the
	///     same implementation under several service types with the same member exposes one instance through all.
	/// </summary>
	public string? Instance { get; set; }

	/// <summary>
	///     Optional resolution key. Several implementations can share a service type under different keys.
	///     Consumers select one with <c>[FromKey]</c>.
	/// </summary>
	public string? Key { get; set; }

	/// <summary>
	///     Marks this as an overridable default, usually declared in a module. It applies only when
	///     nothing else registers the same service, so a container or another module can replace it.
	/// </summary>
	public bool Default { get; set; }

	/// <summary>
	///     Adds this registration only if the service is not already registered. Like <see cref="Default" />,
	///     for contributing a service from a module without overriding an existing one.
	/// </summary>
	public bool TryAdd { get; set; }

	/// <summary>
	///     Constructs the singleton at container build time instead of lazily on first resolve. This is
	///     the synchronous analog of <c>InitializeAsync</c>, which warms only async-initialized singletons.
	///     Eager singletons are built in registration order and disposed with the container like any other.
	/// </summary>
	public bool Eager { get; set; }

	/// <summary>
	///     The name of a <c>static void</c> method on the container accepting a
	///     <typeparamref name="TImplementation" />, invoked once the instance has been constructed - a
	///     synchronous post-construction initialization hook.
	/// </summary>
	public string? OnActivated { get; set; }

	/// <summary>
	///     The name of a <c>static void</c> method on the container accepting a
	///     <typeparamref name="TImplementation" />, invoked when the owning container or scope is disposed -
	///     in reverse creation order and before the instance's own disposal (it runs in addition to, not
	///     instead of, that disposal).
	/// </summary>
	public string? OnRelease { get; set; }
}

#pragma warning restore S2326
