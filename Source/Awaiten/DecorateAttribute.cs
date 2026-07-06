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

/// <summary>
///     Decorates every closing of an open generic service with the matching closing of an open generic decorator,
///     so a pipeline behavior wraps all closings of <c>IHandler&lt;&gt;</c> in one attribute. Resolving
///     <c>IHandler&lt;Order&gt;</c> yields <c>LoggingBehavior&lt;Order&gt;(inner)</c>. Uses <see cref="Type" />
///     arguments because an unbound generic like <c>typeof(IHandler&lt;&gt;)</c> cannot be a type argument.
/// </summary>
/// <remarks>
///     A closed decorator is synthesized for every closing already present in the graph (however it was registered -
///     explicit, scanned or open generic) and interleaves with explicit <c>[Decorate&lt;D, IHandler&lt;Order&gt;&gt;]</c>
///     registrations by the same <see cref="Order" />-then-declaration-order rules. The decorator's arity must equal
///     the service's, and it must expose the service with its type parameters in declaration order
///     (<c>LoggingBehavior&lt;T&gt; : IHandler&lt;T&gt;</c>); a closing whose type arguments violate the decorator's
///     constraints is skipped with a diagnostic. Like the generic form, a decorator's own generic dependencies are
///     supplied only when some other registration expands them into the graph.
/// </remarks>
/// <example><c>[Decorate(typeof(LoggingBehavior&lt;&gt;), typeof(IHandler&lt;&gt;))]</c></example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class DecorateAttribute : Attribute
{
	/// <summary>Decorates every closing of <paramref name="service" /> with the matching closing of <paramref name="decorator" />.</summary>
	/// <param name="decorator">The open generic decorator, e.g. <c>typeof(LoggingBehavior&lt;&gt;)</c>.</param>
	/// <param name="service">The open generic service to decorate, e.g. <c>typeof(IHandler&lt;&gt;)</c>.</param>
	public DecorateAttribute(Type decorator, Type service)
	{
		Decorator = decorator;
		Service = service;
	}

	/// <summary>The open generic decorator type, e.g. <c>typeof(LoggingBehavior&lt;&gt;)</c>.</summary>
	public Type Decorator { get; }

	/// <summary>The open generic service type whose closings are decorated, e.g. <c>typeof(IHandler&lt;&gt;)</c>.</summary>
	public Type Service { get; }

	/// <summary>
	///     The decorator's position in each closing's chain (ascending, outermost last), interleaved with that
	///     closing's explicit decorators by <see cref="Order" /> first, then declaration order.
	/// </summary>
	public int Order { get; set; }
}
