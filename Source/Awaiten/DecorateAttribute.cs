using System;

namespace Awaiten;

// S2326: the type parameters are the source generator's input. It reads the service and decorator types from
// the attribute's type arguments via Roslyn, so TDecorator appears only in the type constraint.
#pragma warning disable S2326

/// <summary>
///     Wraps the registration of <typeparamref name="TService" /> in <typeparamref name="TDecorator" />, so every
///     consumer receives <c>new TDecorator(inner)</c> instead of the bare implementation. The decorator's single
///     <typeparamref name="TService" /> constructor parameter is supplied the decorated registration; its other
///     parameters resolve from the graph as usual. The decorator inherits the decorated registration's lifetime.
/// </summary>
/// <remarks>
///     Multiple decorators chain in declaration order, the last declared being outermost. With
///     <c>[Decorate&lt;D1, IService&gt;]</c> then <c>[Decorate&lt;D2, IService&gt;]</c>, resolving <c>IService</c>
///     yields <c>D2(D1(Real))</c>. Use <see cref="Order" /> to position a decorator explicitly. Decorating a
///     service also decorates every collection view of it, member by member, so the decorator cannot be bypassed.
///     Only the unkeyed registration is decorated; a service registered solely under a <c>[FromKey]</c> key reports AWT123.
///     <para>
///         The type parameters are ordered concrete type first, then service, to match the lifetime attributes.
///     </para>
///     <para>
///         The decorator and the instance it wraps are each owned by the container and disposed independently,
///         outermost first. A decorator that also disposes the inner instance would dispose it twice, so leave the
///         inner's disposal to the container, or make the decorator's <c>Dispose</c> idempotent if it must forward.
///     </para>
/// </remarks>
/// <typeparam name="TDecorator">The decorator implementation. It must have exactly one constructor parameter assignable to <typeparamref name="TService" />.</typeparam>
/// <typeparam name="TService">The service type to decorate.</typeparam>
/// <example><c>[Decorate&lt;LoggingDecorator, IService&gt;]</c></example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class DecorateAttribute<TDecorator, TService> : Attribute
	where TDecorator : class, TService
{
	/// <summary>
	///     The decorator's position in the chain (ascending, outermost last). Decorators are ordered by
	///     <see cref="Order" /> first, then declaration order. Leave unset to chain purely by declaration order.
	/// </summary>
	public int Order { get; set; }
}
