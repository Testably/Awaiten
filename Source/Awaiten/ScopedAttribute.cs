using System;

namespace Awaiten;

// S2326: the type parameter is the source generator's input. It reads the implementation and service
// types from the attribute's type arguments via Roslyn, so the body never references it.
#pragma warning disable S2326

/// <summary>
///     Registers an implementation as scoped through <see cref="Type" /> arguments. With an unbound generic
///     like <c>typeof(Repository&lt;&gt;)</c>, which cannot be a type argument, resolving a closed service such as
///     <c>IRepository&lt;Order&gt;</c> constructs the matching <c>Repository&lt;Order&gt;</c> once per scope for
///     each closed type argument. With closed types it registers exactly like
///     <see cref="ScopedAttribute{TImplementation, TService}" />.
/// </summary>
/// <remarks>
///     On an open generic, <see cref="Factory" /> names a generic method with the service's type parameters,
///     closed per closing.
/// </remarks>
/// <example><c>[Scoped(typeof(Repository&lt;&gt;), typeof(IRepository&lt;&gt;))]</c></example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ScopedAttribute : Attribute
{
	/// <summary>Registers <paramref name="implementation" /> as itself.</summary>
	/// <param name="implementation">The implementation type, e.g. <c>typeof(Repository&lt;&gt;)</c>.</param>
	public ScopedAttribute(Type implementation)
	{
		Implementation = implementation;
		Service = implementation;
	}

	/// <summary>Registers <paramref name="implementation" /> under <paramref name="service" />.</summary>
	/// <param name="implementation">The implementation type, e.g. <c>typeof(Repository&lt;&gt;)</c>.</param>
	/// <param name="service">The service type, e.g. <c>typeof(IRepository&lt;&gt;)</c>.</param>
	public ScopedAttribute(Type implementation, Type service)
	{
		Implementation = implementation;
		Service = service;
	}

	/// <summary>The implementation type to construct, open generic or closed.</summary>
	public Type Implementation { get; }

	/// <summary>The service type under which instances are resolved, open generic or closed.</summary>
	public Type Service { get; }

	/// <summary>
	///     Name of a container method that produces the instance instead of a constructor, with its parameters
	///     resolved from the graph. The result is cached once per scope. For an open generic it is a generic method
	///     with exactly the service's type parameters, in declaration order, closed per closing, such as
	///     <c>static IRepository&lt;T&gt; Create&lt;T&gt;()</c>.
	/// </summary>
	public string? Factory { get; set; }

	/// <summary>
	///     Optional resolution key. Several implementations can share a service type under different keys.
	///     The key flows onto every closed implementation expanded from this registration.
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
	///     registers the same service, so a container or another module can replace it. For an open generic this
	///     holds per closing. <c>Fallback.Warn</c> reports AWT148 if another overridable default competes with
	///     nothing stronger to resolve it; <c>Fallback.Silent</c> defers without a warning. Defaults to
	///     <c>Fallback.None</c>, a normal registration.
	/// </summary>
	public Fallback Fallback { get; set; }

	/// <summary>
	///     The name of a <c>static void</c> method on the container accepting the implementation, invoked once the
	///     instance has been constructed. For an open generic the method may be generic over the service's type
	///     arguments.
	/// </summary>
	public string? OnActivated { get; set; }

	/// <summary>
	///     The name of a <c>static void</c> method on the container accepting the implementation, invoked when the
	///     owning container or scope is disposed, before the instance's own disposal. For an open generic the
	///     method may be generic over the service's type arguments.
	/// </summary>
	public string? OnRelease { get; set; }

	/// <summary>
	///     Suppresses the container's built-in disposal of this instance; releasing it becomes your
	///     responsibility, typically through an <see cref="OnRelease" /> hook. Defaults to
	///     <see langword="false" />. Reported to hosts as
	///     <see cref="AwaitenRegistration.DisposalSuppressed" /> when the instance may be disposable, which a
	///     projection into Microsoft.Extensions.DependencyInjection rejects (AWT214).
	/// </summary>
	public bool SuppressDisposal { get; set; }
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
	///     Defaults to <see langword="false" />, where the container disposes what it builds. Reported to hosts as
	///     <see cref="AwaitenRegistration.DisposalSuppressed" /> when the instance may be disposable, which a
	///     projection into Microsoft.Extensions.DependencyInjection rejects (AWT214).
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
	///     Defaults to <see langword="false" />, where the container disposes what it builds. Reported to hosts as
	///     <see cref="AwaitenRegistration.DisposalSuppressed" /> when the instance may be disposable, which a
	///     projection into Microsoft.Extensions.DependencyInjection rejects (AWT214).
	/// </summary>
	public bool SuppressDisposal { get; set; }
}

#pragma warning restore S2326
