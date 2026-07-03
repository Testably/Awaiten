using Awaiten.SourceGenerators.Entities;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

/// <summary>
///     A single lifetime registration read from a <c>[Singleton]</c>/<c>[Transient]</c>/<c>[Scoped]</c>
///     attribute on a container: the service and implementation type names, the lifetime, the
///     implementation symbol, the attribute's source location, how the instance is produced (a
///     constructor by default, or the container member named by the attribute's <c>Factory</c>/
///     <c>Instance</c> argument), the attribute's optional resolution <c>Key</c> (so several
///     implementations can share one service type), whether the attribute set both directives at
///     once (an error), and - for variance matching - the closed generic service symbol (so a
///     differently-closed consumer request can be redirected to a variance-compatible registration),
///     whether the registration was contributed by a <c>[Scan]</c> (an overridable registration that
///     never conflicts with an explicit one over the same implementation), whether that scan opted
///     into skipping unconstructable matches (<c>SkipUnconstructable</c>, degrading the AWT101 error to
///     the AWT141 warning), whether the registration is an overridable module default (<c>Weak</c>:
///     <c>Default</c> or <c>TryAdd</c>, contributing its service only when nothing stronger claimed it),
///     whether that default was a <c>Default</c> specifically (<c>IsDefault</c>, so two colliding
///     <c>Default</c>s can be surfaced as AWT148 while <c>TryAdd</c> stays silent), and the imported
///     module that declared the registration (<c>Origin</c>, <see langword="null" /> for the container's
///     own registrations - a module's <c>Factory</c>/<c>Instance</c> member is resolved against and
///     emitted qualified with the module type, not the container).
/// </summary>
/// <remarks>
///     <see cref="Location" /> is the live Roslyn location (with its syntax tree), not an equatable
///     <see cref="LocationInfo" />: an analyzer needs the syntax tree for <c>#pragma</c> suppression to
///     apply. This type is intermediate (consumed within a single analysis pass), so it never flows
///     through the generator's incremental cache and does not need to be equatable.
/// </remarks>
internal sealed record RawRegistration(
	string ServiceType,
	string ImplementationType,
	Lifetime Lifetime,
	INamedTypeSymbol Implementation,
	Location? Location,
	ProductionKind Production = ProductionKind.Constructor,
	string? ProductionMember = null,
	bool ConflictingDirectives = false,
	string? Key = null,
	INamedTypeSymbol? ServiceSymbol = null,
	bool IsScan = false,
	bool ScanSkipsUnconstructable = false,
	bool Weak = false,
	bool IsDefault = false,
	INamedTypeSymbol? Origin = null);

/// <summary>
///     A single <c>[Decorate&lt;TDecorator, TService&gt;]</c> registration read from a container: the
///     decorated service type name, the service and decorator symbols, the requested chain
///     <see cref="Order" />, and the declaration index used to break ties between equal orders (so
///     decorators chain in declaration order by default). Collected apart from the lifetime registrations
///     and expanded after coalescing by <c>AwaitenGenerator.BuildDecoratorChains</c> into synthetic-keyed
///     chain links.
/// </summary>
/// <remarks>
///     Like <see cref="RawRegistration" /> this is an intermediate type consumed within a single analysis
///     pass, so it carries the live Roslyn <see cref="Location" /> (not an equatable
///     <c>LocationInfo</c>) and never flows through the generator's incremental cache.
/// </remarks>
internal sealed record DecorateRegistration(
	string Service,
	INamedTypeSymbol ServiceSymbol,
	INamedTypeSymbol Decorator,
	int Order,
	int DeclarationOrder,
	Location? Location);

/// <summary>
///     A single <c>[Composite&lt;TComposite, TService&gt;]</c> registration read from a container: the
///     composed service type name, the service and composite symbols, and the composite's chosen lifetime
///     (defaulting to transient). Collected apart from the lifetime registrations and applied after
///     coalescing (and after decorator chains, so a composite fronts the decorated members) by
///     <c>AwaitenGenerator.BuildComposites</c>, which registers the composite as the public single-dispatch
///     winner while excluding it from collection membership.
/// </summary>
/// <remarks>
///     Like <see cref="RawRegistration" /> this is an intermediate type consumed within a single analysis
///     pass, so it carries the live Roslyn <see cref="Location" /> (not an equatable
///     <c>LocationInfo</c>) and never flows through the generator's incremental cache.
/// </remarks>
internal sealed record CompositeRegistration(
	string Service,
	INamedTypeSymbol ServiceSymbol,
	INamedTypeSymbol Composite,
	Lifetime Lifetime,
	Location? Location);

partial class AwaitenGenerator
{
	/// <summary>
	///     Redirects a decorator instance's inner parameter to the next-lower chain link. <see cref="ParameterServiceType" />
	///     is the parameter's own declared type (as <see cref="ClassifyParameter" /> classifies it), used to pick the single
	///     inner parameter out of the constructor. <see cref="ServiceType" /> is the decorated service type - the type under
	///     which every chain link is registered - so the redirect keys the parameter to <c>(ServiceType, InnerKey)</c> even
	///     when the parameter is declared as a base type of the service (its declared type is not a registration key).
	///     Consumed by <see cref="ClassifyParameters" /> when it classifies the decorator link.
	/// </summary>
	private readonly record struct DecoratorInner(string ParameterServiceType, string ServiceType, string InnerKey);

	private sealed class ImplInfo
	{
		public ImplInfo(
			string implementationType,
			INamedTypeSymbol symbol,
			Lifetime lifetime,
			LocationInfo? location,
			ProductionKind production,
			string? productionMember,
			bool isScan = false,
			INamedTypeSymbol? origin = null)
		{
			ImplementationType = implementationType;
			Symbol = symbol;
			Lifetime = lifetime;
			Location = location;
			Production = production;
			ProductionMember = productionMember;
			IsScan = isScan;
			Origin = origin;
			Services = new List<ServiceKey>();
		}

		public string ImplementationType { get; }
		public INamedTypeSymbol Symbol { get; }
		public Lifetime Lifetime { get; }
		public LocationInfo? Location { get; }
		public ProductionKind Production { get; }
		public string? ProductionMember { get; }

		/// <summary>Whether the first (winning) registration of this implementation came from a <c>[Scan]</c>.</summary>
		public bool IsScan { get; }

		/// <summary>
		///     The imported module that declared the winning registration, or <see langword="null" /> for the
		///     container's own (or a scan's). A module's <c>Factory</c>/<c>Instance</c> member is resolved
		///     against and emitted qualified with this type rather than the container.
		/// </summary>
		public INamedTypeSymbol? Origin { get; }

		public List<ServiceKey> Services { get; }

		/// <summary>
		///     The service type to name this implementation by in a diagnostic: its first winning service, or -
		///     when it won none (a collection member reached only through the collection) - the implementation
		///     type itself, so the message still identifies it rather than crashing on an empty service list.
		/// </summary>
		public string OwningServiceOrImpl => Services.Count > 0 ? Services[0].Service : ImplementationType;
	}
}
