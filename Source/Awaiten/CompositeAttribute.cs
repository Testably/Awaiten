using System;

namespace Awaiten;

// S2326: the type parameters are the source generator's input. It reads the composite and service types from the
// attribute's type arguments via Roslyn, so neither appears in the attribute body (only in the type constraint).
#pragma warning disable S2326

/// <summary>
///     Exposes <typeparamref name="TComposite" /> as the single public <typeparamref name="TService" />, fanning
///     out to every other registration of <typeparamref name="TService" />. The composite's collection constructor
///     parameter (<c>IEnumerable&lt;TService&gt;</c>, <c>TService[]</c>, …) receives all the other members, never
///     itself, so every consumer reaches the whole set through one façade.
/// </summary>
/// <remarks>
///     The composite is excluded from everyone's collection membership: a consumer requesting
///     <c>IEnumerable&lt;TService&gt;</c> still gets the bare members, while a plain <typeparamref name="TService" />
///     parameter gets the composite. A composite over zero other registrations fans out to an empty collection. A
///     composite whose constructor has no collection parameter of the composed service reports AWT130.
///     <para>The type parameters are ordered concrete type first, then service, to match the lifetime attributes and <see cref="DecorateAttribute{TDecorator,TService}" />.</para>
///     <para>
///         The composite defaults to <see cref="AwaitenLifetime.Transient" />, re-materializing its member array on
///         every resolve while each member keeps its own lifetime. Set <see cref="Lifetime" /> to share one instance.
///         A singleton composite over a shorter-lived scoped member is the usual captive case (AWT105).
///     </para>
/// </remarks>
/// <typeparam name="TComposite">The composite implementation. It must have a constructor parameter that is a collection of <typeparamref name="TService" />.</typeparam>
/// <typeparam name="TService">The service type the composite fronts.</typeparam>
/// <example><c>[Composite&lt;CompositeNotifier, INotifier&gt;]</c></example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CompositeAttribute<TComposite, TService> : Attribute
	where TComposite : class, TService
{
	/// <summary>
	///     The composite's lifetime. Defaults to <see cref="AwaitenLifetime.Transient" />, so the composite
	///     re-materializes its member array on every resolve. Members keep their own lifetimes regardless.
	/// </summary>
	public AwaitenLifetime Lifetime { get; set; } = AwaitenLifetime.Transient;
}
