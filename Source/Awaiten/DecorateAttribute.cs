using System;

namespace Awaiten;

// S2326: the type parameters are the Awaiten source generator's input — it reads the service and
// decorator types from the attribute's type arguments via Roslyn symbols, so TDecorator is
// intentionally not referenced in the attribute body (only in the type constraint).
#pragma warning disable S2326

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
///     <c>[Decorate]</c> declared is the outermost. With <c>[Decorate&lt;D1, IService&gt;]</c>
///     followed by <c>[Decorate&lt;D2, IService&gt;]</c>, resolving <c>IService</c> yields
///     <c>D2(D1(Real))</c>. Use <see cref="Order" /> to position a decorator explicitly rather than
///     by declaration order. Decorating a service also decorates every collection
///     (<c>IEnumerable&lt;IService&gt;</c>, <c>IService[]</c>, …) view of it, so the decorator cannot
///     be bypassed: a service with several registrations is decorated member by member.
///     <para>
///         The type parameters are ordered <typeparamref name="TDecorator" /> then
///         <typeparamref name="TService" /> - the concrete type first, then the service it is exposed
///         as - to match the lifetime attributes (<c>[Transient&lt;TImplementation, TService&gt;]</c>
///         and friends).
///     </para>
///     <para>
///         The decorator and the implementation it wraps are <em>each</em> owned by the container and
///         disposed independently, outermost first (a decorator is built after its inner, so it is
///         disposed before it). A decorator that also disposes the inner instance handed to it would
///         therefore dispose it twice, so leave the inner's disposal to the container - or make the
///         decorator's <c>Dispose</c> idempotent if it must forward.
///     </para>
/// </remarks>
/// <typeparam name="TDecorator">
///     The decorator implementation; it must have exactly one constructor parameter assignable to
///     <typeparamref name="TService" />.
/// </typeparam>
/// <typeparam name="TService">The service type to decorate.</typeparam>
/// <example><c>[Decorate&lt;LoggingDecorator, IService&gt;]</c></example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class DecorateAttribute<TDecorator, TService> : Attribute
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
