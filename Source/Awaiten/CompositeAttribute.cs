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

/// <summary>
///     Fronts every closing of an open generic service with the matching closing of an open generic composite, so
///     one attribute installs a façade over all closings of <c>IHandler&lt;&gt;</c>. Resolving
///     <c>IHandler&lt;Order&gt;</c> yields the <c>CompositeHandler&lt;Order&gt;</c> fanning out to the other
///     <c>IHandler&lt;Order&gt;</c> registrations. Uses <see cref="Type" /> arguments because an unbound generic
///     like <c>typeof(IHandler&lt;&gt;)</c> cannot be a type argument.
/// </summary>
/// <remarks>
///     A closed composite is synthesized for every closing already present in the graph. The composite's arity must
///     equal the service's, and it must expose the service with its type parameters in declaration order
///     (<c>CompositeHandler&lt;T&gt; : IHandler&lt;T&gt;</c>); a closing whose type arguments violate the composite's
///     constraints is skipped with a diagnostic. Each closing then follows the closed <c>[Composite&lt;_, _&gt;]</c>
///     rules (one façade per closing, excluded from its own fan-out).
/// </remarks>
/// <example><c>[Composite(typeof(CompositeHandler&lt;&gt;), typeof(IHandler&lt;&gt;))]</c></example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CompositeAttribute : Attribute
{
	/// <summary>Fronts every closing of <paramref name="service" /> with the matching closing of <paramref name="composite" />.</summary>
	/// <param name="composite">The open generic composite, e.g. <c>typeof(CompositeHandler&lt;&gt;)</c>.</param>
	/// <param name="service">The open generic service to front, e.g. <c>typeof(IHandler&lt;&gt;)</c>.</param>
	public CompositeAttribute(Type composite, Type service)
	{
		Composite = composite;
		Service = service;
	}

	/// <summary>The open generic composite type, e.g. <c>typeof(CompositeHandler&lt;&gt;)</c>.</summary>
	public Type Composite { get; }

	/// <summary>The open generic service type whose closings are fronted, e.g. <c>typeof(IHandler&lt;&gt;)</c>.</summary>
	public Type Service { get; }

	/// <summary>
	///     The composite's lifetime, applied to every synthesized closing. Defaults to
	///     <see cref="AwaitenLifetime.Transient" />, so each closing re-materializes its member array on every resolve.
	/// </summary>
	public AwaitenLifetime Lifetime { get; set; } = AwaitenLifetime.Transient;
}
