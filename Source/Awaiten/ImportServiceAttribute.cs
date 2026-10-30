using System;

namespace Awaiten;

// S2326: the type parameter is the source generator's input. It reads the external service type from the
// attribute's type argument via Roslyn, so the body never references it.
#pragma warning disable S2326

/// <summary>
///     Declares that the <see cref="ContainerAttribute">container</see> draws the single service type
///     <typeparamref name="TService" /> from an external provider: every dependency of that type is satisfied
///     from the container's <see cref="IExternalResolver" /> instead of being reported as a missing dependency
///     (AWT101). This is the typed counterpart of the blanket <see cref="ImportServicesAttribute" />.
/// </summary>
/// <remarks>
///     <para>
///         Unlike the blanket <see cref="ImportServicesAttribute" /> - which routes only unkeyed <em>direct</em>
///         dependencies with no registration - an explicit <c>[ImportService&lt;T&gt;]</c> is stronger and narrower.
///         It routes <em>every</em> unregistered dependency of type <typeparamref name="TService" />, keyed or not,
///         whether a constructor parameter, a factory-method parameter, or an injected property, to the external
///         provider, forwarding any <see cref="FromKeyAttribute">[FromKey]</see> key to the resolver. Every other
///         unresolved dependency still gets the ordinary missing-dependency check (AWT101), so a forgotten or
///         mistyped dependency of any other type is still reported rather than silently deferred to the host.
///     </para>
///     <para>
///         Only the <em>direct</em> dependency of type <typeparamref name="TService" /> is routed. A relationship or
///         collection over it - <c>Func&lt;TService&gt;</c>, <c>Lazy&lt;TService&gt;</c>, <c>Task&lt;TService&gt;</c>,
///         <c>IEnumerable&lt;TService&gt;</c> and the like - is not, since the external resolver hands back an
///         instance rather than a deferred or fanned-out shape. Such a dependency still resolves from the Awaiten
///         graph and, with no registration, surfaces as AWT101 (or an empty collection).
///     </para>
///     <para>
///         Honored on the <see cref="ContainerAttribute">container</see> and on any imported <c>[Module]</c>,
///         symmetric with <see cref="ImportServicesAttribute" />. Combine with <see cref="FromKeyAttribute" /> at the
///         dependency to resolve a keyed external service; the key is forwarded to the resolver. Declaring a type both
///         <c>[ImportService&lt;T&gt;]</c> and registering it on the container is contradictory (AWT175), since a type
///         is either host-owned or Awaiten-owned, not both; declaring a type external that no dependency in the graph
///         consumes is a dead declaration (AWT176).
///     </para>
/// </remarks>
/// <typeparam name="TService">The service type the container draws from the external provider.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ImportServiceAttribute<TService> : Attribute
{
}

#pragma warning restore S2326

/// <summary>
///     Declares that the <see cref="ContainerAttribute">container</see> draws the <see cref="Service" /> type from an
///     external provider. A closed type behaves exactly like <see cref="ImportServiceAttribute{TService}" />; an
///     unbound generic definition such as <c>typeof(ILogger&lt;&gt;)</c> routes every closing of it that a dependency
///     asks for, so one declaration covers <c>ILogger&lt;OrderService&gt;</c>, <c>ILogger&lt;PaymentService&gt;</c>
///     and so on. Uses a <see cref="Type" /> argument because an unbound generic cannot be a type argument.
/// </summary>
/// <remarks>
///     <para>
///         Every direct dependency of a matching type, keyed or not, whether a constructor parameter, a factory-method
///         parameter, or an injected property, is satisfied from the container's <see cref="IExternalResolver" />,
///         forwarding any <see cref="FromKeyAttribute">[FromKey]</see> key. A relationship or collection over a matching
///         type (<c>Func&lt;T&gt;</c>, <c>Lazy&lt;T&gt;</c>, <c>Task&lt;T&gt;</c>, <c>IEnumerable&lt;T&gt;</c> and the
///         like) is not routed. Each concrete closing the graph consumes is listed in
///         <see cref="IAwaitenRoot.ExternalDependencies" />.
///     </para>
///     <para>
///         Honored on the <see cref="ContainerAttribute">container</see> and on any imported <c>[Module]</c>. A routed
///         type that is also registered on the container is contradictory (AWT175), a declaration no dependency
///         consumes is dead (AWT176), and a type that is neither closed nor an unbound generic definition is rejected
///         (AWT215).
///     </para>
/// </remarks>
/// <example><c>[ImportService(typeof(ILogger&lt;&gt;))]</c></example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ImportServiceAttribute : Attribute
{
	/// <summary>Draws <paramref name="service" />, or every closing of it when it is an unbound generic definition, from the external provider.</summary>
	/// <param name="service">The closed service type, e.g. <c>typeof(IClock)</c>, or an unbound generic definition, e.g. <c>typeof(ILogger&lt;&gt;)</c>.</param>
	public ImportServiceAttribute(Type service)
	{
		Service = service;
	}

	/// <summary>The closed service type or unbound generic definition drawn from the external provider.</summary>
	public Type Service { get; }
}
