using System;

namespace Awaiten;

/// <summary>
///     Wraps the registration of <typeparamref name="TService" /> in
///     <typeparamref name="TDecorator" />, so every consumer of <typeparamref name="TService" />
///     receives <c>new TDecorator(inner)</c> instead of the bare implementation. The decorator's
///     single constructor parameter of type <typeparamref name="TService" /> is supplied the
///     decorated registration (its other constructor parameters resolve from the graph as usual),
///     and the decorator inherits the decorated registration's lifetime.
/// </summary>
/// <remarks>
///     Multiple decorators of the same service chain in declaration order: the last
///     <c>[Decorate]</c> declared is the outermost. With <c>[Decorate&lt;IService, D1&gt;]</c>
///     followed by <c>[Decorate&lt;IService, D2&gt;]</c>, resolving <c>IService</c> yields
///     <c>D2(D1(Real))</c>. Use <see cref="Order" /> to position a decorator explicitly rather than
///     by declaration order. Decorating a service also decorates every collection
///     (<c>IEnumerable&lt;IService&gt;</c>, <c>IService[]</c>, …) view of it, so the decorator cannot
///     be bypassed: a service with several registrations is decorated member by member.
/// </remarks>
/// <typeparam name="TService">The service type to decorate.</typeparam>
/// <typeparam name="TDecorator">
///     The decorator implementation; it must have exactly one constructor parameter assignable to
///     <typeparamref name="TService" />.
/// </typeparam>
/// <example><c>[Decorate&lt;IService, LoggingDecorator&gt;]</c></example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class DecorateAttribute<TService, TDecorator> : Attribute
	where TDecorator : class, TService
{
	/// <summary>
	///     The decorator's position in the chain (ascending, outermost last). Decorators are ordered
	///     by <see cref="Order" /> first, then by declaration order, so a lower value sits closer to the
	///     decorated implementation and a higher value closer to the consumer. Leave unset to chain
	///     purely by declaration order.
	/// </summary>
	public int Order { get; set; }
}
