using System;

namespace Awaiten;

// S2326: the type parameter is the source generator's input. It reads the implementation and service
// types from the attribute's type arguments via Roslyn, so the body never references it.
#pragma warning disable S2326

/// <summary>
///     Registers an open generic implementation as scoped. Resolving a closed service such as
///     <c>IRepository&lt;Order&gt;</c> constructs the matching <c>Repository&lt;Order&gt;</c> once per
///     scope for each closed type argument. Uses <see cref="Type" /> arguments because an unbound generic
///     like <c>typeof(Repository&lt;&gt;)</c> cannot be a type argument.
/// </summary>
/// <example><c>[Scoped(typeof(Repository&lt;&gt;), typeof(IRepository&lt;&gt;))]</c></example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ScopedAttribute : Attribute
{
	/// <summary>Registers the open generic <paramref name="implementation" /> as itself.</summary>
	/// <param name="implementation">The open generic concrete type, e.g. <c>typeof(Repository&lt;&gt;)</c>.</param>
	public ScopedAttribute(Type implementation)
	{
		Implementation = implementation;
		Service = implementation;
	}

	/// <summary>Registers the open generic <paramref name="implementation" /> under the open generic <paramref name="service" />.</summary>
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
	///     Optional resolution key. Several implementations can share a service type under different keys.
	///     The key flows onto every closed implementation expanded from this registration.
	/// </summary>
	public object? Key { get; set; }
}

/// <summary>
///     Registers <typeparamref name="TImplementation" /> as scoped on the
///     <see cref="ContainerAttribute">container</see>. The service type is the implementation itself.
/// </summary>
/// <remarks>
///     A scoped registration resolves to one instance per scope created via <c>CreateScope</c>. The container
///     is the root scope. With <see cref="Factory" />, <typeparamref name="TImplementation" /> may be an
///     interface or abstract type: the container constructs nothing then, so the type argument only names the
///     service the produced instance is resolved as.
/// </remarks>
/// <typeparam name="TImplementation">
///     The type to construct and resolve; concrete unless <see cref="Factory" /> produces the instance.
/// </typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ScopedAttribute<TImplementation> : Attribute
	where TImplementation : class
{
	/// <summary>
	///     Name of a container method that produces the instance instead of a constructor. It may be
	///     static or instance, must return <typeparamref name="TImplementation" />, and has its
	///     parameters resolved from the graph. The result is cached once per scope.
	/// </summary>
	public string? Factory { get; set; }

	/// <summary>
	///     Optional resolution key. Several implementations can share a service type under different keys.
	///     Consumers select one with <c>[FromKey]</c>.
	/// </summary>
	public object? Key { get; set; }

	/// <summary>
	///     Restricts this registration to the constructor parameters and [Inject] properties of the given consumer
	///     type: the service resolves to this implementation only when injected into <see cref="WhenInjectedInto" />,
	///     and to the unconditional registration everywhere else (contextual binding).
	/// </summary>
	public Type? WhenInjectedInto { get; set; }

	/// <summary>
	///     Marks this as an overridable default, usually declared in a module: it applies only when nothing else
	///     registers the same service, so a container or another module can replace it. <c>Fallback.Warn</c>
	///     reports AWT148 if another overridable default competes with nothing stronger to resolve it;
	///     <c>Fallback.Silent</c> defers without a warning. Defaults to <c>Fallback.None</c>, a normal registration.
	/// </summary>
	public Fallback Fallback { get; set; }

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

	/// <summary>
	///     Suppresses the container's built-in disposal of this instance. The container still constructs it
	///     (by constructor or <see cref="Factory" />), but does not call its <c>Dispose</c>/<c>DisposeAsync</c>
	///     on teardown; releasing it becomes your responsibility - typically through an <see cref="OnRelease" />
	///     hook (for example, returning it to a pool), or because its lifetime is owned outside the container.
	///     Defaults to <see langword="false" />, where the container disposes what it builds.
	/// </summary>
	public bool SuppressDisposal { get; set; }
}

/// <summary>
///     Registers <typeparamref name="TImplementation" /> as scoped exposed through
///     <typeparamref name="TService" />.
/// </summary>
/// <remarks>A scoped registration resolves to one instance per scope created via <c>CreateScope</c>. The container is the root scope.</remarks>
/// <typeparam name="TImplementation">The concrete type to construct.</typeparam>
/// <typeparam name="TService">The service type under which the instance is resolved.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ScopedAttribute<TImplementation, TService> : Attribute
	where TImplementation : class, TService
{
	/// <summary>
	///     Name of a container method that produces the instance instead of a constructor. It may be
	///     static or instance, must return <typeparamref name="TImplementation" />, and has its
	///     parameters resolved from the graph. The result is cached once per scope. Registering the same
	///     implementation under several service types with the same factory shares one instance.
	/// </summary>
	public string? Factory { get; set; }

	/// <summary>
	///     Optional resolution key. Several implementations can share a service type under different keys.
	///     Consumers select one with <c>[FromKey]</c>.
	/// </summary>
	public object? Key { get; set; }

	/// <summary>
	///     Restricts this registration to the constructor parameters and [Inject] properties of the given consumer
	///     type: the service resolves to this implementation only when injected into <see cref="WhenInjectedInto" />,
	///     and to the unconditional registration everywhere else (contextual binding).
	/// </summary>
	public Type? WhenInjectedInto { get; set; }

	/// <summary>
	///     Marks this as an overridable default, usually declared in a module: it applies only when nothing else
	///     registers the same service, so a container or another module can replace it. <c>Fallback.Warn</c>
	///     reports AWT148 if another overridable default competes with nothing stronger to resolve it;
	///     <c>Fallback.Silent</c> defers without a warning. Defaults to <c>Fallback.None</c>, a normal registration.
	/// </summary>
	public Fallback Fallback { get; set; }

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

	/// <summary>
	///     Suppresses the container's built-in disposal of this instance. The container still constructs it
	///     (by constructor or <see cref="Factory" />), but does not call its <c>Dispose</c>/<c>DisposeAsync</c>
	///     on teardown; releasing it becomes your responsibility - typically through an <see cref="OnRelease" />
	///     hook (for example, returning it to a pool), or because its lifetime is owned outside the container.
	///     Defaults to <see langword="false" />, where the container disposes what it builds.
	/// </summary>
	public bool SuppressDisposal { get; set; }
}

#pragma warning restore S2326
