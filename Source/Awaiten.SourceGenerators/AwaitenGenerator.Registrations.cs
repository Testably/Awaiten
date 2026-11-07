using Awaiten.SourceGenerators.Entities;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

/// <summary>
///     A single <c>[Singleton]</c>/<c>[Transient]</c>/<c>[Scoped]</c> registration on a container: the service
///     and implementation, lifetime, source location, production (constructor, or a <c>Factory</c>/<c>Instance</c>
///     member), optional <c>Key</c>, and the closed generic service symbol for variance matching. The flags mark
///     provenance and precedence: <c>IsScan</c> / <c>ScanSkipsUnconstructable</c> (a <c>[Scan]</c> contribution),
///     <c>Weak</c> / <c>IsDefault</c> (an overridable module default, filling only a gap; <c>IsDefault</c> is
///     Fallback.Warn, which reports AWT148 on colliding defaults), <c>Origin</c> (the imported module that
///     declared it), <c>IsSynthesized</c> (from open generic
///     expansion, yielding to explicit registrations), and <c>Eager</c> (build-time construction, singletons only).
///     <c>WhenInjectedInto</c> names a consumer type for a contextual binding: the registration is stored under a
///     synthetic context key and reached only from that consumer's constructor parameters (AWT167 when it never applies).
///     <c>GreedyConstructor</c> marks a same-compilation module-scan match, constructed through the greediest
///     accessible constructor (the one a cross-assembly module's generated factory mirrors) rather than the
///     registered-services-aware pick, so the match constructs identically wherever the module is imported from.
///     <c>GenericFactory</c> marks a closing of an open generic <c>typeof</c> registration whose <c>Factory</c> is a
///     generic method, closed with the implementation's type arguments when it is resolved.
///     <c>Traits</c> describes a self-compiled implementation produced by a module's generated factory, which the
///     factory's return type hides: <c>TraitsExact</c> when read off the implementation itself (a module
///     registration), else only the interfaces a module scan reported, so a hidden disposable is still checked at run time.
///     <c>ConstructVia</c> names the generated factory of the module or base container that builds an implementation
///     the container cannot construct, and <c>Constructor</c> the constructor that factory mirrors when it is fixed
///     (a base container's factory mirrors the constructor the base picked).
///     <c>ScanImplementation</c> is the implementation a module's generated scan factory in another assembly builds,
///     which lets the container share one instance with every other registration of it (see <c>ShareScannedInstances</c>).
///     <c>Unlisted</c> marks a dependency-only service, the generated handle of a service a module of another assembly
///     registers under an internal type, which the container never offers for resolution, and <c>Hidden</c> the
///     implementation such a handle holds.
/// </summary>
/// <remarks>
///     <see cref="Location" /> is the live Roslyn location (with its syntax tree), not an equatable
///     <see cref="LocationInfo" />, because an analyzer needs the syntax tree for <c>#pragma</c> suppression. This
///     type is intermediate (one analysis pass), so it never flows through the generator's incremental cache.
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
	INamedTypeSymbol? Origin = null,
	bool IsSynthesized = false,
	bool Eager = false,
	string? OnActivated = null,
	string? OnRelease = null,
	bool SuppressDisposal = false,
	string? WhenInjectedInto = null,
	bool GreedyConstructor = false,
	IReadOnlyList<INamedTypeSymbol>? OnActivatedMarkers = null,
	IReadOnlyList<INamedTypeSymbol>? OnReleaseMarkers = null,
	bool GenericFactory = false,
	ImplementationTraits? Traits = null,
	bool TraitsExact = false,
	string? ConstructVia = null,
	string? ConstructViaType = null,
	IMethodSymbol? Constructor = null,
	INamedTypeSymbol? ScanImplementation = null,
	bool Unlisted = false,
	INamedTypeSymbol? Hidden = null);

/// <summary>
///     An imported module: its symbol and the location of the container's <c>[Import]</c> attribute that
///     pulled it in. The location doubles as the diagnostic fallback for module attributes read from a
///     referenced assembly, which carry no syntax.
/// </summary>
/// <remarks>
///     An intermediate type carrying a live Roslyn <see cref="Location" />; see <see cref="RawRegistration" />.
///     <see cref="Inherited" /> marks a contribution of an extended base container (the base itself, flagged
///     <see cref="IsBase" />, or one of its imports), which a registration declared directly on the extending
///     container replaces. Their location is the container's <c>Extends</c> argument unless the base imports the
///     module from source.
/// </remarks>
internal sealed record ImportedModule(INamedTypeSymbol Symbol, Location? ImportLocation, bool Inherited = false, bool IsBase = false);

/// <summary>
///     A single <c>[Decorate&lt;TDecorator, TService&gt;]</c> registration: the decorated service, the service and
///     decorator symbols, the chain <see cref="Order" />, the declaration index breaking ties and the selected
///     <see cref="Keys" /> (a <see langword="null" /> entry is the unkeyed registration, a <see langword="null" />
///     list is <c>AllKeys</c>). Expanded after coalescing into synthetic-keyed chain links. <see cref="Inherited" />
///     marks a decorator of an extended base, which stays inside every decorator of the extending container.
///     <see cref="Owner" /> is the module or base that declares it, whose async initializer conventions judge it, or
///     <see langword="null" /> for the container's own.
/// </summary>
/// <remarks>An intermediate type carrying a live Roslyn <see cref="Location" />; see <see cref="RawRegistration" />.</remarks>
internal sealed record DecorateRegistration(
	string Service,
	INamedTypeSymbol ServiceSymbol,
	INamedTypeSymbol Decorator,
	int Order,
	int DeclarationOrder,
	Location? Location,
	IReadOnlyList<string?>? Keys,
	bool Inherited = false,
	OwnerFactoryCall? Via = null,
	INamedTypeSymbol? Owner = null);

/// <summary>
///     The generated factory of a module or base container (<see cref="Owner" />) that builds a decorator or composite
///     the container cannot construct: its name, the accessible type it returns, and the method whose parameters the
///     container resolves, which is the factory itself when read from metadata, or the constructor it mirrors when the
///     owner generates it in the same run.
/// </summary>
/// <remarks>An intermediate type carrying live Roslyn symbols; see <see cref="RawRegistration" />.</remarks>
internal sealed record OwnerFactoryCall(INamedTypeSymbol Owner, string Name, string ReturnType, IMethodSymbol Producer)
{
	/// <summary>The qualified factory the generated container calls.</summary>
	public string Call => $"{Owner.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{AwaitenGenerator.InternalsClass}.{Name}";
}

/// <summary>
///     A single <c>[Composite&lt;TComposite, TService&gt;]</c> registration: the composed service, the service and
///     composite symbols, the lifetime (default transient) and the fronted <see cref="Key" />
///     (<see langword="null" /> for the unkeyed registrations). Applied after coalescing (and decorator chains) as
///     the public single-dispatch winner, excluded from collection membership.
/// </summary>
/// <remarks>An intermediate type carrying a live Roslyn <see cref="Location" />; see <see cref="RawRegistration" />.</remarks>
internal sealed record CompositeRegistration(
	string Service,
	INamedTypeSymbol ServiceSymbol,
	INamedTypeSymbol Composite,
	Lifetime Lifetime,
	Location? Location,
	string? Key,
	OwnerFactoryCall? Via = null,
	INamedTypeSymbol? Owner = null);

/// <summary>
///     A single open generic <c>[Decorate(typeof(D&lt;&gt;), typeof(IService&lt;&gt;))]</c> registration: the unbound
///     service and decorator definitions, the chain <see cref="Order" /> and the declaration index (shared with the
///     closed <c>[Decorate&lt;,&gt;]</c> forms so open and closed decorators interleave) and the selected <see cref="Keys" />
///     (as on <see cref="DecorateRegistration" />). Expanded into a closed <see cref="DecorateRegistration" /> per
///     existing closing of the service.
/// </summary>
/// <remarks>An intermediate type carrying a live Roslyn <see cref="Location" />; see <see cref="RawRegistration" />.</remarks>
internal sealed record OpenDecorateRegistration(
	INamedTypeSymbol Service,
	INamedTypeSymbol Decorator,
	int Order,
	int DeclarationOrder,
	Location? Location,
	IReadOnlyList<string?>? Keys,
	bool Inherited = false,
	INamedTypeSymbol? Owner = null);

/// <summary>
///     A single open generic <c>[Composite(typeof(C&lt;&gt;), typeof(IService&lt;&gt;))]</c> registration: the unbound
///     service and composite definitions, the lifetime and the fronted <see cref="Key" />. Expanded into a closed
///     <see cref="CompositeRegistration" /> per existing closing of the service.
/// </summary>
/// <remarks>An intermediate type carrying a live Roslyn <see cref="Location" />; see <see cref="RawRegistration" />.</remarks>
internal sealed record OpenCompositeRegistration(
	INamedTypeSymbol Service,
	INamedTypeSymbol Composite,
	Lifetime Lifetime,
	Location? Location,
	string? Key,
	INamedTypeSymbol? Owner = null);

partial class AwaitenGenerator
{
	/// <summary>
	///     Redirects a decorator instance's inner parameter to the next-lower chain link.
	///     <see cref="ParameterServiceType" /> is the parameter's own declared type (used to pick the single inner
	///     parameter), and <see cref="ServiceType" /> is the decorated service the links are registered under, so the
	///     redirect keys to <c>(ServiceType, InnerKey)</c> even when the parameter is declared as a base of the service.
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
			string? productionMember)
		{
			ImplementationType = implementationType;
			Symbol = symbol;
			Lifetime = lifetime;
			Location = location;
			Production = production;
			ProductionMember = productionMember;
			Services = new List<ServiceKey>();
		}

		public string ImplementationType { get; }
		public INamedTypeSymbol Symbol { get; }
		public Lifetime Lifetime { get; }
		public LocationInfo? Location { get; }
		public ProductionKind Production { get; }
		public string? ProductionMember { get; }

		/// <summary>Whether the first (winning) registration of this implementation came from a <c>[Scan]</c>.</summary>
		public bool IsScan { get; init; }

		/// <summary>Whether this is the instance of a <c>[Composite]</c>.</summary>
		public bool IsComposite { get; init; }

		/// <summary>
		///     Whether construction picks the greediest accessible constructor unconditionally (the one a
		///     cross-assembly module's generated factory mirrors) instead of the registered-services-aware pick.
		///     Set for a same-compilation module-scan match, so the module's matches construct identically
		///     wherever the module is imported from.
		/// </summary>
		public bool GreedyConstructor { get; init; }

		/// <summary>
		///     Whether the winning registration opted into eager build-time construction (<c>Eager = true</c>).
		///     Set from the first registration like the implementation's other options; honored only for a
		///     singleton (see <c>BuildInstance</c>).
		/// </summary>
		public bool Eager { get; init; }

		/// <summary>
		///     The identities of the outermost decorator chain links wrapping this implementation, one per decorated
		///     slot (see <c>DecoratorChainBuilder</c>). Empty when it is not decorated.
		/// </summary>
		public List<string> OutermostDecorators { get; } = new();

		/// <summary>
		///     The imported module that declared the winning registration, or <see langword="null" /> for the
		///     container's own (or a scan's). A module's <c>Factory</c>/<c>Instance</c> member is resolved
		///     against and emitted qualified with this type rather than the container.
		/// </summary>
		public INamedTypeSymbol? Origin { get; init; }

		/// <summary>
		///     The module or base container that declares a decorator or composite, or <see langword="null" />. Its
		///     <c>[AsyncInitializer&lt;T&gt;]</c> conventions judge the instance (see <see cref="ConventionScope" />).
		/// </summary>
		public INamedTypeSymbol? DeclaredBy { get; init; }

		/// <summary>
		///     The scope whose <c>[AsyncInitializer&lt;T&gt;]</c> conventions judge this instance: the module or base that
		///     declares it, else <paramref name="container" />.
		/// </summary>
		public INamedTypeSymbol ConventionScope(INamedTypeSymbol container) => DeclaredBy ?? Origin ?? container;

		/// <summary>
		///     The names of the <c>static void M(TImplementation)</c> container methods to run once the instance
		///     is constructed (<see cref="OnActivated" />) and when its owner is disposed (<see cref="OnRelease" />),
		///     or <see langword="null" /> when no registration named one. Set from the winning registration; for a
		///     scanned implementation a later scan's hook fills a slot the winner left unset (see
		///     <c>MergeScanHooks</c>, AWT199 when two scans contradict). Resolved against the container in
		///     <c>BuildInstance</c> (AWT164 when no matching method exists).
		/// </summary>
		public string? OnActivated { get; set; }

		/// <inheritdoc cref="OnActivated" />
		public string? OnRelease { get; set; }

		/// <summary>
		///     The module whose registration named the <see cref="OnActivated" /> hook, or <see langword="null" />
		///     for a container-named one. A hook name is owner-relative, and two scan registrations with different
		///     origins can fill one implementation's two slots (see <c>MergeScanHooks</c>), so each slot resolves
		///     against its own origin rather than the winning registration's <see cref="Origin" />.
		/// </summary>
		public INamedTypeSymbol? OnActivatedOrigin { get; set; }

		/// <inheritdoc cref="OnActivatedOrigin" />
		public INamedTypeSymbol? OnReleaseOrigin { get; set; }

		/// <summary>
		///     The closed marker forms the <see cref="OnActivated" /> hook could bind its type arguments from: the
		///     union, across every scan registration of this implementation naming that same hook, of the closings
		///     its <c>[Scan(typeof(IView&lt;&gt;))]</c> open-generic marker contributed. Kept per hook slot (its
		///     twin is <see cref="OnReleaseMarkers" />) so a second scan hooking the other slot does not widen this
		///     one's set. Exactly one bindable closing (say <c>MainWindow : IView&lt;IMainViewModel&gt;</c> carries
		///     <c>IView&lt;IMainViewModel&gt;</c>) dispatches a generic hook as <c>WireView&lt;IMainViewModel&gt;</c>;
		///     more than one leaves its type arguments ambiguous (AWT198), decided in <c>ResolveHook</c> where the
		///     hook's arity and constraints are known. A closing of an open generic <c>typeof</c> registration with a
		///     hook contributes its closed service the same way. Empty otherwise (a closed or markerless scan, a closed
		///     registration, or no hook), so a generic hook has nothing to bind and is unusable while a non-generic
		///     hook is unaffected.
		/// </summary>
		public List<INamedTypeSymbol> OnActivatedMarkers { get; } = new();

		/// <inheritdoc cref="OnActivatedMarkers" />
		public List<INamedTypeSymbol> OnReleaseMarkers { get; } = new();

		/// <summary>
		///     Whether the winning registration opted out of the container's built-in disposal
		///     (<c>SuppressDisposal = true</c>): the container constructs the instance but never calls its
		///     <c>Dispose</c>/<c>DisposeAsync</c>, leaving teardown to an <see cref="OnRelease" /> hook or an
		///     owner outside the container. Set from the first registration like the implementation's other options.
		/// </summary>
		public bool SuppressDisposal { get; init; }

		/// <summary>
		///     Whether <see cref="ProductionMember" /> names a generic factory of an open generic <c>typeof</c>
		///     registration, closed with <see cref="Symbol" />'s type arguments (see <c>ResolveFactory</c>).
		/// </summary>
		public bool GenericFactory { get; init; }

		/// <summary>
		///     How a self-compiled implementation behind a module's generated factory is initialized and disposed,
		///     which the factory's return type hides, or <see langword="null" /> for any other implementation. See
		///     <see cref="RawRegistration" />.
		/// </summary>
		public ImplementationTraits? Traits { get; init; }

		/// <inheritdoc cref="RawRegistration" />
		public bool TraitsExact { get; init; }

		/// <summary>
		///     The qualified generated factory of the module that declares this implementation privately, which a
		///     same-compilation container calls instead of the constructor it cannot reach, or <see langword="null" />.
		///     <see cref="ConstructViaType" /> is the service the factory returns.
		/// </summary>
		public string? ConstructVia { get; init; }

		/// <inheritdoc cref="ConstructVia" />
		public string? ConstructViaType { get; init; }

		/// <summary>
		///     The constructor the generated factory behind <see cref="ConstructVia" /> mirrors, when the factory fixes it, or
		///     <see langword="null" /> to select one as usual.
		/// </summary>
		public IMethodSymbol? Constructor { get; init; }

		/// <inheritdoc cref="RawRegistration" />
		public bool Unlisted { get; init; }

		/// <inheritdoc cref="RawRegistration" />
		public INamedTypeSymbol? Hidden { get; init; }

		public List<ServiceKey> Services { get; }

		/// <summary>
		///     The service type to name this implementation by in a diagnostic: its first winning service, or the
		///     implementation type itself when it won none (a collection member reached only through the
		///     collection), so the message still identifies it rather than crashing on an empty service list.
		/// </summary>
		public string OwningServiceOrImpl => Services.Count > 0 ? Services[0].Service : RegisteredType(ImplementationType);
	}
}
