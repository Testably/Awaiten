using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

/// <summary>
///     The diagnostics reported by the <see cref="AwaitenGenerator" />. Stable <c>AWT</c> ids in the
///     <c>Awaiten</c> category.
/// </summary>
internal static class Diagnostics
{
	/// <summary>
	///     A required dependency (a constructor parameter of a registered type) has no registration on
	///     the container.
	/// </summary>
	public static readonly DiagnosticDescriptor MissingDependency = new(
		"AWT101",
		"Missing dependency",
		"'{0}' cannot be resolved: '{1}' requires '{2}', which is not registered on the container",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A dependency cycle exists in the object graph.
	/// </summary>
	public static readonly DiagnosticDescriptor DependencyCycle = new(
		"AWT102",
		"Dependency cycle",
		"Dependency cycle detected: {0}",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A registered implementation type cannot be instantiated (it is abstract or an interface).
	/// </summary>
	public static readonly DiagnosticDescriptor NotInstantiable = new(
		"AWT103",
		"Implementation is not instantiable",
		"'{0}' cannot be used as an implementation: it is abstract or an interface. Register a concrete type.",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A registered implementation type has no constructor the container can call.
	/// </summary>
	public static readonly DiagnosticDescriptor NoAccessibleConstructor = new(
		"AWT104",
		"No accessible constructor",
		"'{0}' has no accessible constructor for the container to call",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A singleton depends, directly or transitively through transients, on a shorter-lived scoped
	///     service, which it would capture for the container's lifetime.
	/// </summary>
	public static readonly DiagnosticDescriptor CaptiveDependency = new(
		"AWT105",
		"Captive dependency",
		"Captive dependency: the singleton '{0}' depends on '{1}', which has the shorter Scoped lifetime and would be captured for the container's lifetime",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A synchronous <c>Factory</c> method's body provably constructs (or returns a local of) a concrete
	///     type that implements <c>IAsyncInitializable</c>, while its declared return type does not expose it.
	///     The container reads async-initialization taint off the declared return type, so it cannot see that
	///     the produced instance needs initialization, so the hidden <c>InitializeAsync</c> never runs and the
	///     instance is handed out uninitialized. A hidden <c>IDisposable</c> is <em>not</em> reported: the
	///     container disposes factory outputs behind a runtime check, so it does not leak. An asynchronous
	///     <c>Task&lt;T&gt;</c> / <c>ValueTask&lt;T&gt;</c> factory is <em>not</em> reported either: it owns its
	///     own initialization (the container awaits the factory itself). This is a best-effort lint (Warning):
	///     it fires only when the concrete returned type is statically provable from the body, so it has false
	///     negatives by design (a helper-returned or runtime-selected implementation is not reported), but is
	///     intended to have no false positives.
	/// </summary>
	public static readonly DiagnosticDescriptor FactoryHidesAsyncInitialization = new(
		"AWT106",
		"Factory hides asynchronous initialization behind its declared return type",
		"factory '{0}' constructs '{1}', which is async-initialized, but declares return type '{2}'; the container cannot see that it needs initialization, so its InitializeAsync never runs. Return '{1}', or make the factory 'async Task<{2}>' and handle initialization inside it.",
		"Awaiten",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <summary>
	///     The same implementation is registered with more than one lifetime; coalescing into a single
	///     instance would silently drop one of the declared lifetimes.
	/// </summary>
	public static readonly DiagnosticDescriptor ConflictingLifetime = new(
		"AWT107",
		"Conflicting lifetime",
		"'{0}' is registered with conflicting lifetimes: {1} and {2}. Register the implementation with a single lifetime.",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A <c>Factory</c> registration names a member that is not a usable factory method on its owner -
	///     the container, or the declaring module for an imported registration (it is missing, is not a
	///     method, or does not return the registered service type).
	/// </summary>
	public static readonly DiagnosticDescriptor InvalidFactory = new(
		"AWT108",
		"Invalid factory",
		"'{0}' cannot be produced: {2} has no accessible method '{1}' returning '{0}'",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     An <c>Instance</c> registration names a member that is not a usable instance member on its owner -
	///     the container, or the declaring module for an imported registration (it is missing, is not a field
	///     or property, or is not assignable to the service type).
	/// </summary>
	public static readonly DiagnosticDescriptor InvalidInstance = new(
		"AWT109",
		"Invalid instance member",
		"'{0}' cannot be exposed: {2} has no accessible field or property '{1}' of type '{0}'",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A single registration sets both <c>Factory</c> and <c>Instance</c>; the two directives are
	///     mutually exclusive and the generator cannot tell which production to use.
	/// </summary>
	public static readonly DiagnosticDescriptor ConflictingProductionDirectives = new(
		"AWT110",
		"Conflicting production directives",
		"'{0}' sets both Factory and Instance. Specify exactly one of them.",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     The same implementation is registered with more than one production (a different kind -
	///     constructor, factory or instance, or the same kind naming a different member); coalescing into
	///     a single instance would silently drop one of them.
	/// </summary>
	public static readonly DiagnosticDescriptor ConflictingProduction = new(
		"AWT111",
		"Conflicting production",
		"'{0}' is registered with conflicting production strategies ({1} and {2}). Register the implementation with a single production strategy.",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A <c>Factory</c> registration names a method that is overloaded: more than one accessible
	///     method of that name returns the registered service type, so the choice would be order-dependent.
	/// </summary>
	public static readonly DiagnosticDescriptor AmbiguousFactory = new(
		"AWT112",
		"Ambiguous factory",
		"'{0}' has an ambiguous factory: {2} has more than one accessible method '{1}' returning '{0}'. Give the factory method a unique name.",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A <c>Func&lt;TArg…, T&gt;</c> relationship's runtime arguments do not match the
	///     <c>[Arg]</c>-marked constructor parameters of the service it produces, in order.
	/// </summary>
	public static readonly DiagnosticDescriptor RuntimeArgumentMismatch = new(
		"AWT113",
		"Runtime argument mismatch",
		"The Func<…, {0}> relationship supplies runtime arguments ({1}) that do not match the [Arg] parameters ({2}) of '{0}'",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A service with <c>[Arg]</c>-marked parameters is built fresh from its runtime arguments on every
	///     request, so its declared lifetime (other than <c>Transient</c>) cannot be honored.
	/// </summary>
	public static readonly DiagnosticDescriptor ParameterizedLifetime = new(
		"AWT114",
		"Parameterized service must be transient",
		"'{0}' has [Arg] parameters and is built fresh from its runtime arguments on every request, so its '{1}' lifetime cannot be honored; register it as Transient",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A parameterized service (one with <c>[Arg]</c>-marked parameters) is requested as a plain
	///     dependency or a <c>Lazy&lt;T&gt;</c>, neither of which can supply its runtime arguments. It is
	///     resolvable only through a <c>Func&lt;TArg…, T&gt;</c> relationship.
	/// </summary>
	public static readonly DiagnosticDescriptor ParameterizedRequiresFunc = new(
		"AWT115",
		"Parameterized service requires a Func factory",
		"'{1}' cannot depend on '{0}' directly: '{0}' has [Arg] parameters and can only be obtained through a Func<…, {0}> relationship that supplies them",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A <c>[Container]</c> class is not declared <c>static</c>. The container is a pure definition and
	///     its factory/instance members must be static; the usable instance is the generated <c>Root</c>.
	/// </summary>
	public static readonly DiagnosticDescriptor NonStaticContainer = new(
		"AWT116",
		"Container must be static",
		"'{0}' must be a static class. The [Container] class is a definition whose factory and instance members are static; create the container with 'new {0}.Root()'.",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     Two different implementations are registered under the same service type and key, so a keyed
	///     resolution of that key would be ambiguous.
	/// </summary>
	public static readonly DiagnosticDescriptor DuplicateKey = new(
		"AWT117",
		"Duplicate keyed registration",
		"'{0}' is registered more than once with key '{1}'; a keyed resolution would be ambiguous",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A root-owned instance (a singleton or pre-built instance), directly or through its transitive
	///     transient dependencies, holds a <c>Func&lt;…&gt;</c> over a build-on-demand service (a transient or
	///     parameterized service) whose construction tracks a fresh disposable on the root: the produced
	///     service is itself disposable, or it transitively rebuilds a disposable transient. Each call to that
	///     factory builds and re-tracks those disposables on the container's root, so they accumulate for its
	///     entire lifetime, an unbounded leak. The leak-free remedy is the <c>{2}</c> message argument, since it
	///     differs by relationship: a synchronous <c>Func&lt;…&gt;</c> is redirected to a
	///     <c>Func&lt;…, Owned&lt;T&gt;&gt;</c> disposal handle (draining into a throwaway scope), while an
	///     asynchronous <c>Func&lt;…, Task&lt;T&gt;&gt;</c> cannot use <c>Owned&lt;T&gt;</c> (a synchronous handle
	///     that cannot await initialization, AWT119), so it is pointed at an explicitly scoped resolution instead.
	/// </summary>
	/// <remarks>
	///     Unlike the retired per-registration check, this is flow-based: it fires only for the statically
	///     visible root-accumulating pattern, not for every disposable transient. A disposable transient
	///     reached only from a scope (or through <c>Owned&lt;T&gt;</c>) is bounded and is not reported. This
	///     descriptor is the loose-lifetime-safety form: a warning that can be suppressed in source; strict
	///     lifetime safety reports <see cref="RootAccumulatingFactoryStrict" /> instead.
	/// </remarks>
	public static readonly DiagnosticDescriptor RootAccumulatingFactory = new(
		"AWT118",
		"Factory accumulates disposables on the container root",
		"'{1}' holds a Func over '{0}', which is built on demand; the instances it builds - and the disposables created while constructing them - are tracked on the container root and accumulate for its lifetime; {2}",
		"Awaiten",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <summary>
	///     The strict-lifetime-safety form of <see cref="RootAccumulatingFactory">AWT118</see>: the same
	///     diagnostic, reported at error severity (by the analyzer) and carrying
	///     <see cref="WellKnownDiagnosticTags.NotConfigurable" /> so it cannot be silenced by
	///     <c>#pragma warning disable</c>, <c>&lt;NoWarn&gt;</c> or an editorconfig severity override. The only
	///     way to opt out is to set <c>LifetimeSafety.Loose</c> on the <c>[Container]</c>, which switches back
	///     to the suppressible <see cref="RootAccumulatingFactory" />. This is what makes the root-accumulation
	///     leak structurally impossible under strict lifetime safety rather than merely warned-about.
	/// </summary>
	/// <remarks>
	///     Its declared default severity is <see cref="DiagnosticSeverity.Warning" /> (identical to
	///     <see cref="RootAccumulatingFactory" />), so that release tracking sees a single, consistent AWT118.
	///     The analyzer raises it to an error per report. <see cref="WellKnownDiagnosticTags.NotConfigurable" />,
	///     not the severity, is what makes it non-suppressible.
	/// </remarks>
	public static readonly DiagnosticDescriptor RootAccumulatingFactoryStrict = new(
		"AWT118",
		RootAccumulatingFactory.Title,
		RootAccumulatingFactory.MessageFormat,
		RootAccumulatingFactory.Category,
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: RootAccumulatingFactory.Description,
		helpLinkUri: RootAccumulatingFactory.HelpLinkUri,
		WellKnownDiagnosticTags.NotConfigurable);

	/// <summary>
	///     A synchronous <c>Func&lt;T&gt;</c> / <c>Lazy&lt;T&gt;</c> / <c>Owned&lt;T&gt;</c> relationship
	///     targets a service whose implementation is <c>IAsyncInitializable</c>: it would resolve the service
	///     without awaiting its initialization. Resolve it through <c>ResolveAsync</c>, or set
	///     <c>SyncResolveAfterInit</c> on the <c>[Container]</c> to allow synchronous resolution after warm-up.
	/// </summary>
	public static readonly DiagnosticDescriptor SynchronousAsyncResolution = new(
		"AWT119",
		"Synchronous resolution of an async-initialized service",
		"'{0}' resolves '{2}' through a synchronous {1}<>, but '{2}' is async-initialized; resolve it through ResolveAsync, or set SyncResolveAfterInit on the [Container]",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A synchronous <c>Func&lt;T&gt;</c> / <c>Lazy&lt;T&gt;</c> / <c>Owned&lt;T&gt;</c> relationship
	///     targets a service that is not itself async-initialized but reaches one through its non-deferred
	///     dependencies, so resolving it synchronously would hand back an instance whose async dependencies
	///     were never initialized.
	/// </summary>
	public static readonly DiagnosticDescriptor AsyncDependencyOnSyncPath = new(
		"AWT120",
		"Async-tainted service reached synchronously",
		"'{0}' resolves an async-tainted service synchronously: {1}. Resolve it through ResolveAsync, or set SyncResolveAfterInit on the [Container].",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     An <c>Owned&lt;T&gt;</c> disposal handle is requested through a <c>Lazy&lt;Owned&lt;T&gt;&gt;</c> or
	///     <c>Lazy&lt;Task&lt;Owned&lt;T&gt;&gt;&gt;</c> relationship. <c>Lazy</c> does not unwrap
	///     <c>Owned&lt;T&gt;</c> (memoizing a single disposal handle would hand back a disposed handle after the
	///     first teardown), so the handle's inner type is treated as the service, which is not registered. This
	///     reports that mismatch with the supported owned forms instead of a bare "missing dependency" for the
	///     <c>Owned&lt;T&gt;</c> type itself.
	/// </summary>
	public static readonly DiagnosticDescriptor OwnedThroughLazy = new(
		"AWT121",
		"Owned<T> handle requested through Lazy",
		"'{0}' cannot be resolved: '{1}' requires '{2}' through a Lazy relationship, but an Owned<T> disposal handle cannot be produced through Lazy<…>; request it directly as an Owned<T>, or through Func<…, Owned<T>>, Task<Owned<T>> or Func<…, Task<Owned<T>>>",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A collection dependency (<c>IEnumerable&lt;T&gt;</c> and friends) has a member whose implementation
	///     is async-initialized or async-tainted, but the collection is materialized synchronously. The
	///     synchronous shapes build every member eagerly into an array, with no place to await an asynchronous
	///     initialization. Consume the collection through a shape that awaits its members
	///     (<c>IAsyncEnumerable&lt;T&gt;</c>, or an awaited <c>Task&lt;IReadOnlyList&lt;T&gt;&gt;</c> and friends),
	///     warm the graph with <c>SyncResolveAfterInit</c> on the <c>[Container]</c> (after
	///     <c>InitializeAsync</c>), or restructure so the collection holds no async-tainted members.
	/// </summary>
	public static readonly DiagnosticDescriptor AsyncCollectionResolution = new(
		"AWT122",
		"Async-tainted service reached synchronously through a collection",
		"'{0}' resolves the collection of '{1}' synchronously, but the member '{2}' is async-tainted; a synchronous collection is materialized eagerly, with no place to await an initialization - consume it as IAsyncEnumerable<T> or Task<IReadOnlyList<T>> (which await each member), set SyncResolveAfterInit on the [Container] (and resolve after InitializeAsync), or remove the async member from the collection",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A <c>[Decorate&lt;TDecorator, TService&gt;]</c> names a service that has no registration to
	///     decorate, so there is no inner implementation to wrap.
	/// </summary>
	public static readonly DiagnosticDescriptor DecoratedServiceNotRegistered = new(
		"AWT123",
		"Decorated service not registered",
		"'{0}' is decorated by '{1}' but has no registration to decorate; register the service before decorating it",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A decorator's constructor has no parameter assignable to the decorated service type, or more than
	///     one ambiguous one, so Awaiten cannot tell which parameter receives the inner instance
	///     (e.g. <c>LoggingDecorator(IService a, IService b)</c> or one that takes no <c>IService</c> at all).
	/// </summary>
	public static readonly DiagnosticDescriptor DecoratorMissingInnerParameter = new(
		"AWT124",
		"Decorator has no single inner parameter",
		"The decorator '{0}' must have exactly one constructor parameter assignable to the decorated service '{1}'; it has none or several",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     An open generic registration's implementation and service have different arity, so no closed
	///     service can be mapped onto the implementation's type parameters (e.g. <c>Repository&lt;,&gt;</c>
	///     declared for <c>IRepository&lt;&gt;</c>). v1 matches the open form exactly, so the arities must be equal.
	/// </summary>
	public static readonly DiagnosticDescriptor OpenGenericArityMismatch = new(
		"AWT125",
		"Open generic arity mismatch",
		"The open generic registration of '{0}' for '{1}' is invalid: the implementation has {2} type parameter(s) but the service has {3}; their arity must match",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A closed generic required from the graph cannot be constructed from an open generic
	///     registration because its type arguments violate the implementation's type-parameter
	///     constraints (e.g. <c>Repository&lt;int&gt;</c> where the implementation declares <c>where T : class</c>).
	///     An error because the dependency is then unsatisfiable. The decorator/composite analogue, where the base
	///     service still resolves, is the warning <see cref="DecoratorClosingConstraintViolation">AWT171</see>.
	/// </summary>
	public static readonly DiagnosticDescriptor OpenGenericConstraintViolation = new(
		"AWT126",
		"Open generic constraint violation",
		"'{0}' cannot be constructed from the open generic '{1}': the type argument(s) violate its type-parameter constraints",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     The <c>typeof</c>-argument form of a lifetime attribute exists for open generics and must receive an
	///     unbound generic type (<c>typeof(Repository&lt;&gt;)</c>). A closed generic
	///     (<c>typeof(Repository&lt;int&gt;)</c>) would be silently reduced to its open definition, and a
	///     non-generic type would match no closed service, so both are rejected in favor of the generic
	///     attribute form (<c>[Transient&lt;Repository&lt;int&gt;, IRepository&lt;int&gt;&gt;]</c>).
	/// </summary>
	public static readonly DiagnosticDescriptor OpenGenericNotUnbound = new(
		"AWT127",
		"Open generic registration is not an unbound generic",
		"The open generic registration must use an unbound open generic type such as typeof(Repository<>); '{0}' is not an unbound generic type - register a closed or non-generic type with the generic attribute form instead",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A closed service is mapped onto an open generic implementation by applying its type arguments to the
	///     implementation's type parameters positionally, which is only correct when the implementation exposes
	///     the service with its own type parameters in declaration order (<c>Repository&lt;T&gt; : IRepository&lt;T&gt;</c>).
	///     A reordered or remapped implementation (<c>Repository&lt;TKey, TValue&gt; : IRepository&lt;TValue, TKey&gt;</c>)
	///     would construct a closed type that does not satisfy the requested service, so it is rejected.
	/// </summary>
	public static readonly DiagnosticDescriptor OpenGenericServiceRemapped = new(
		"AWT128",
		"Open generic type parameters are remapped",
		"The open generic implementation '{0}' does not expose the service '{1}' with its type parameters in declaration order, so a closed service cannot be mapped onto it by position; the implementation's type parameters must map one-to-one, in order, onto the service's",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     Open generic expansion nested deeper than the supported limit, which almost always means the
	///     registrations form an unbounded generic recursion: a closed implementation whose constructor
	///     depends on an ever-larger closed generic of the same open registration
	///     (<c>Node&lt;T&gt;</c> depending on <c>Node&lt;List&lt;T&gt;&gt;</c>). Expansion is stopped at the
	///     limit so the generator terminates rather than looping until it exhausts memory.
	/// </summary>
	public static readonly DiagnosticDescriptor OpenGenericExpansionTooDeep = new(
		"AWT129",
		"Open generic expansion is too deep",
		"Open generic expansion reached '{0}' at nesting depth {1} and was stopped; the open generic registrations likely form an unbounded recursion (an implementation depending on an ever-larger closed generic of the same registration)",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A <c>[Composite&lt;TComposite, TService&gt;]</c> implementation has no constructor parameter that is a
	///     collection (<c>IEnumerable&lt;TService&gt;</c>, <c>IReadOnlyList&lt;TService&gt;</c>, <c>TService[]</c>, …)
	///     of the composed service, so there is nothing for the composite to fan out to.
	/// </summary>
	public static readonly DiagnosticDescriptor CompositeMissingCollectionParameter = new(
		"AWT130",
		"Composite has no collection parameter",
		"The composite '{0}' must have a constructor parameter that is a collection of the composed service '{1}'; it has none",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     The composite type is also registered as an ordinary member of the service it composes (e.g. a
	///     <c>[Transient&lt;C, S&gt;]</c> alongside <c>[Composite&lt;C, S&gt;]</c>). A composite is excluded from
	///     its own fan-out, so that bare registration has no effect and is dropped; a warning rather than an error
	///     because the resulting graph is well-defined (the composite simply never appears in its own collection).
	/// </summary>
	public static readonly DiagnosticDescriptor CompositeAlsoRegisteredAsMember = new(
		"AWT131",
		"Composite is also registered as a member of its own service",
		"The composite '{0}' is also registered as a member of the composed service '{1}'; a composite is excluded from its own fan-out, so that registration has no effect and is ignored",
		"Awaiten",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <summary>
	///     More than one <c>[Composite&lt;_, TService&gt;]</c> names the same service. A service can have at most
	///     one composite (the single public façade), so the first-declared composite wins and the rest are
	///     reported. Declaring the same composite type twice for one service is idempotent and not reported.
	/// </summary>
	public static readonly DiagnosticDescriptor MultipleCompositesForService = new(
		"AWT132",
		"Multiple composites for one service",
		"The service '{0}' has more than one composite; a service can have at most one composite façade, so all but the first are ignored",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A <c>[Composite&lt;TComposite, TService&gt;]</c> has a collection constructor parameter, but its element
	///     type is a base (or otherwise related) type of the composed service rather than the composed service
	///     itself. Collections are resolved by exact element type, so such a parameter would fan out over a
	///     different collection than <c>TService</c>'s registrations, never the intended members.
	/// </summary>
	public static readonly DiagnosticDescriptor CompositeCollectionNotOfComposedService = new(
		"AWT133",
		"Composite collection parameter is not of the composed service",
		"The composite '{0}' fans out over a collection of '{1}', not the composed service '{2}'; its collection parameter's element type must be exactly the composed service",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A container-side composition attribute (a lifetime registration, <c>[Scan]</c>, <c>[Decorate]</c>,
	///     <c>[Composite]</c>, <c>[Import]</c>, <c>[ImportService]</c>/<c>[ImportServices]</c>, or
	///     <c>[InjectProperty]</c>) is applied to a class in an assembly that declares no <c>[Container]</c>.
	///     Composition belongs on the <c>[Container]</c> composition root (or an imported <c>[Module]</c>); a
	///     domain or application assembly should stay free of Awaiten's composition attributes. The consumer-side
	///     escape hatches <c>[Arg]</c>, <c>[FromKey]</c> and <c>[Inject]</c> are permitted on domain types and are
	///     not reported. Reported by <see cref="AwaitenBoundaryAnalyzer" /> (not the generator) so it is
	///     suppressible in source; best-effort, since it stays silent in an assembly that also declares the
	///     <c>[Container]</c> (a single-project app), where the cross-assembly boundary is better enforced by an
	///     architecture test.
	/// </summary>
	public static readonly DiagnosticDescriptor CompositionAttributeOutsideRoot = new(
		"AWT134",
		"Composition attribute outside a composition root",
		"'{0}' carries the composition attribute [{1}], but this assembly declares no [Container]; registration and composition belong on the [Container] (or an imported [Module]), not on domain code",
		"Awaiten",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <summary>
	///     A resolver seam (<c>IAwaitenResolver</c>, <c>IAwaitenAsyncResolver</c>, <c>IAwaitenScope</c>,
	///     <c>IAwaitenRoot</c>, or <c>IAwaitenContainerMetadata</c>) is injected into a type that is not a
	///     <c>[Container]</c> composition root (nor a <c>[Module]</c>), as a constructor parameter or property (the
	///     container never populates a field, so a resolver-typed field is not reported). Resolving from the
	///     container at run time is the Service Locator anti-pattern: it hides a type's
	///     real dependencies and defeats the compile-time graph check. Inject the dependency the type actually
	///     needs instead; only the composition root should hold the resolver. The typed fast-path
	///     <c>IAwaitenResolver&lt;T&gt;</c> is a single-service seam (closer to <c>Func&lt;T&gt;</c>) and is not
	///     reported. Reported by <see cref="AwaitenBoundaryAnalyzer" /> (not the generator) so a deliberate seam,
	///     such as the MS.DI bridge adapter, can be suppressed in source.
	/// </summary>
	public static readonly DiagnosticDescriptor ServiceLocatorInjection = new(
		"AWT135",
		"Service locator: a resolver interface is injected into a service",
		"'{0}' takes the resolver interface '{1}'; resolving from the container at run time hides real dependencies and defeats the compile-time graph check. Inject the dependency you need instead; only the [Container] composition root should hold '{1}'.",
		"Awaiten",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <summary>
	///     A property marked <c>[Inject]</c> has no <c>set</c> or <c>init</c> accessor the container can assign
	///     through the object initializer (the container is not a derived context, so a protected/private-protected
	///     setter, and a cross-assembly internal one, is out of reach), so there is nothing for Awaiten to fill.
	/// </summary>
	public static readonly DiagnosticDescriptor InjectedPropertyNotSettable = new(
		"AWT136",
		"Injected property is not settable",
		"The property '{0}' on '{1}' is marked [Inject] but has no set or init accessor the container can assign through; give it an accessible init/set accessor or remove [Inject]",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     An injected property is marked <c>[Arg]</c>, but runtime arguments flow only through a
	///     <c>Func&lt;…&gt;</c> factory into <c>[Arg]</c> constructor parameters, never through property
	///     injection, which resolves entirely from the graph.
	/// </summary>
	public static readonly DiagnosticDescriptor InjectedPropertyIsArg = new(
		"AWT137",
		"Injected property cannot be a runtime argument",
		"The injected property '{0}' on '{1}' is marked [Arg], but runtime arguments are supplied only through a Func<…> factory to constructor parameters, never to an injected property",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A <c>[Scan]</c> matched no concrete type assignable to its marker in the container's assembly, so
	///     the scan contributes nothing, usually a typo in the marker or an empty marker.
	/// </summary>
	public static readonly DiagnosticDescriptor ScanMatchedNothing = new(
		"AWT138",
		"Assembly scan matched nothing",
		"No concrete types assignable to '{0}' were found in this assembly to register",
		"Awaiten",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <summary>
	///     A <c>[Scan(As = ScanAs.Marker)]</c> matched a concrete type that implements no interface assignable to
	///     the scanned marker, so the match contributes no registration, typically a base-class marker. Use a
	///     marker interface, or add the <c>ScanAs.Self</c> flag to keep the self registration.
	/// </summary>
	public static readonly DiagnosticDescriptor ScanNoImplementedInterfaces = new(
		"AWT139",
		"Assembly scan matched a type with no interface assignable to the marker",
		"'{0}' matched the scan but implements no interface assignable to '{1}', so it is not registered; scan a marker interface or add the ScanAs.Self flag",
		"Awaiten",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <summary>
	///     A <c>[Scan(InAssembliesOf = …)]</c> names an assembly that holds no concrete type assignable to the
	///     scanned marker, so the scan of that assembly contributes nothing, most often a missing
	///     <c>ProjectReference</c> or the wrong marker type.
	/// </summary>
	public static readonly DiagnosticDescriptor ScanAssemblyHasNoCandidates = new(
		"AWT140",
		"Assembly scan target has no candidate types",
		"The assembly '{0}' named by InAssembliesOf has no concrete type to scan for this marker; check that the project is referenced and the marker type is correct",
		"Awaiten",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <summary>
	///     A <c>[Scan(SkipUnconstructable = true)]</c> matched a concrete type the container cannot construct
	///     (a dependency with no registration, or no accessible constructor), so the match is skipped instead of
	///     failing the build. A scan sweeps every assignable concrete class, so the opt-in degrades an
	///     incidental unconstructable match to this warning; without it the match stays the AWT101 error.
	/// </summary>
	public static readonly DiagnosticDescriptor ScanMatchSkipped = new(
		"AWT141",
		"Assembly scan skipped an unconstructable match",
		"'{0}' matched the scan but cannot be constructed ({1}), so it is not registered",
		"Awaiten",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <summary>
	///     Two <c>[Scan]</c> attributes match the same implementation with different lifetimes; the first
	///     scan's lifetime wins (coalescing keeps the first registration), so the contradiction is surfaced
	///     rather than silently resolved by attribute order. An explicit registration of the implementation is
	///     not reported: a scan is overridable and deliberately yields to it.
	/// </summary>
	public static readonly DiagnosticDescriptor ScanLifetimeConflict = new(
		"AWT142",
		"Scans register one implementation with conflicting lifetimes",
		"'{0}' is matched by scans with conflicting lifetimes ({1} and {2}); the first scan's {1} is used",
		"Awaiten",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <summary>
	///     A <c>[Scan(InAssembliesOf = …)]</c> resolved to no assembly at all (an empty array, or entries that
	///     name no type), so the scan can register nothing. An error rather than a warning: unlike a marker that
	///     merely matches nothing (AWT138), an empty assembly list means the scan cannot even look anywhere, so
	///     the attribute itself is malformed. Reported instead of silently falling back to the container's own
	///     assembly, which is what an unset <c>InAssembliesOf</c> means.
	/// </summary>
	public static readonly DiagnosticDescriptor ScanAssembliesEmpty = new(
		"AWT143",
		"Assembly scan names no assembly",
		"This scan's InAssembliesOf names no assembly to scan, so the scan registers nothing; list one type from each assembly to scan",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A property marked <c>[Inject(Deferred = true)]</c> has only an <c>init</c> accessor, or is
	///     <c>required</c>. A deferred property is assigned after construction (to break a cycle), which an
	///     <c>init</c>-only accessor forbids, and it is omitted from the emitted object initializer, which a
	///     <c>required</c> member does not allow (the generated construction would fail with CS9035 inside
	///     generated code). Give it a plain <c>set</c> accessor and drop <c>required</c>.
	/// </summary>
	public static readonly DiagnosticDescriptor DeferredPropertyIsInitOnly = new(
		"AWT144",
		"Deferred property is init-only or required",
		"The property '{0}' on '{1}' is marked [Inject(Deferred = true)] but is {2}; a deferred property is assigned after construction and omitted from the object initializer, so it needs a plain set accessor and must not be required (init accessors and required members can only be satisfied inside an object initializer)",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     Every participant in a cycle of <c>[Inject(Deferred = true)]</c> properties is a transient. A deferred
	///     property breaks a cycle only because a participant is cached before it is wired, so a re-entrant resolve
	///     returns that cached instance; when every participant is a transient nothing is cached anywhere, so each
	///     lap rebuilds the participants and the cycle recurses forever at runtime. Make at least one participant
	///     singleton or scoped, or break the cycle.
	/// </summary>
	public static readonly DiagnosticDescriptor DeferredTransientCycle = new(
		"AWT145",
		"Non-terminating deferred transient cycle",
		"Deferred property cycle in which every participant is a transient detected: {0}. A deferred property breaks a cycle only when a participant is cached (singleton or scoped) before it is wired, so a re-entrant resolve returns that cached instance; when every participant is a transient nothing is cached anywhere, so this cycle would recurse forever. Make at least one participant singleton or scoped, or break the cycle.",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A cycle of <c>[Inject(Deferred = true)]</c> properties includes an async-tainted participant (one that
	///     is <c>IAsyncInitializable</c>, produced by an async factory, or reaches one). A deferred property
	///     breaks a cycle only when a re-entrant resolve returns the already-cached instance; an async resolver
	///     publishes its memoized task only after that re-entrant resolve has returned, so the cycle overflows the
	///     stack or deadlocks at runtime rather than terminating. Break the cycle, or make its participants
	///     synchronous.
	/// </summary>
	public static readonly DiagnosticDescriptor DeferredAsyncCycle = new(
		"AWT146",
		"Non-terminating deferred async cycle",
		"Deferred property cycle through an async-initialized service detected: {0}. A deferred property breaks a cycle only when a re-entrant resolve returns the already-cached instance; an async resolver publishes its memoized task only after that re-entrant resolve has returned, so this cycle would overflow the stack or deadlock at runtime. Break the cycle, or make its participants synchronous (not IAsyncInitializable and not dependent on an async service).",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A cycle that involves at least one <c>[Inject(Deferred = true)]</c> property is only partly broken: it
	///     still traverses a construction-time edge (a constructor parameter, a plain <c>[Inject]</c> property, or a
	///     bare eager <c>Owned&lt;T&gt;</c>/<c>Task&lt;T&gt;</c>) that misbehaves at runtime. A construction edge
	///     whose source is a cached (singleton/scoped) participant resolves its target (and, around the cycle, the
	///     source itself again) <em>before</em> the source is cached, so the source is constructed twice (a
	///     duplicate of a cached instance). When every participant is a transient, nothing is ever cached and the
	///     re-entry recurses forever instead. Only a construction edge that starts at a <em>transient</em> in a
	///     cycle that also has a synchronously-cached participant terminates (the cached participant's re-entrant
	///     resolve returns the cached instance), and that shape is supported and not reported. Turn the offending
	///     construction edge into an <c>[Inject(Deferred = true)]</c> property, or break the cycle another way.
	/// </summary>
	public static readonly DiagnosticDescriptor DeferredMixedCycle = new(
		"AWT147",
		"Deferred cycle retains a construction edge",
		"The cycle {0} is only partly broken by a deferred property: it still has at least one construction-time edge (a constructor parameter, a plain [Inject] property, or an eager Owned<T>/Task<T>). Resolution that traverses that edge re-enters a participant before it is cached, so it constructs a duplicate of a cached (singleton/scoped) participant - or recurses forever when every participant is a transient. To fix it, turn the remaining constructor parameter (or plain [Inject] property) into an [Inject(Deferred = true)] property, or remove one of the dependencies to break the cycle.",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     Two overridable <c>Default</c> registrations provide the same service and neither is overridden by a
	///     strong (non-default) registration, so which default applies is decided only by declaration order. A
	///     warning rather than an error: the graph still resolves (the first-declared default wins), but the
	///     ambiguity is likely unintended: mark one as the winner with a strong registration, or use
	///     <c>Fallback.Silent</c> to opt out of the warning.
	/// </summary>
	public static readonly DiagnosticDescriptor AmbiguousDefault = new(
		"AWT148",
		"Ambiguous default registration",
		"'{0}' has more than one overridable default registration and none overrides the others; the first declared wins",
		"Awaiten",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <summary>
	///     An <c>[Import]</c> names a type that is not marked <c>[Module]</c>. Only modules can be imported, so
	///     the target contributes nothing and is skipped, most likely the wrong type was named. An error rather
	///     than a warning: an import that pulls in no registrations is silently useless, so the mistake is caught
	///     here rather than surfacing later as a cascade of missing dependencies.
	/// </summary>
	public static readonly DiagnosticDescriptor ImportNotAModule = new(
		"AWT149",
		"Import target is not a module",
		"'{0}' is imported but is not marked [Module], so nothing is imported from it; only [Module] types can be imported",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     An imported <c>[Module]</c> carries its own <c>[Import]</c>, which is not followed: module imports are
	///     resolved one level deep, so a module cannot re-export another module's registrations. An error rather
	///     than a warning: leaving it as a warning would silently drop the nested module's registrations, so the
	///     container is forced to import the nested module directly instead.
	/// </summary>
	public static readonly DiagnosticDescriptor NestedModuleImport = new(
		"AWT150",
		"Nested module import not followed",
		"The imported module '{0}' has its own [Import], which is not followed; import the nested module directly, because module imports are resolved one level deep",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     An imported <c>[Module]</c> declares no <c>[Singleton]</c>/<c>[Transient]</c>/<c>[Scoped]</c>
	///     registrations, so the import contributes nothing, most likely the module is incomplete or the wrong
	///     type was named.
	/// </summary>
	public static readonly DiagnosticDescriptor EmptyModule = new(
		"AWT151",
		"Imported module has no registrations",
		"The imported module '{0}' declares no registrations, so it contributes nothing",
		"Awaiten",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <summary>
	///     An imported <c>[Module]</c> class is not declared <c>static</c>. Like a container, a module is a
	///     pure definition (registrations plus static factory and instance members) and is never
	///     instantiated, so it must be a static class (mirroring <see cref="NonStaticContainer">AWT116</see>).
	/// </summary>
	public static readonly DiagnosticDescriptor NonStaticModule = new(
		"AWT152",
		"Module must be static",
		"'{0}' must be a static class. A [Module] class is a definition whose factory and instance members are static; it is imported, never instantiated.",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A module registration's <c>Factory</c>/<c>Instance</c> member exists on the module but is not
	///     accessible from the generated container (e.g. a private member of a source module). Unlike the
	///     container's own members (which the generated partial can reach at any accessibility), a module's
	///     members are called from outside the module, so they must be public, or internal within the
	///     container's assembly (or one granting it internals). An internal member of another assembly
	///     without <c>InternalsVisibleTo</c> is not even imported into the compilation's symbol tables, so
	///     that case surfaces as the not-found AWT108/AWT109 instead.
	/// </summary>
	public static readonly DiagnosticDescriptor InaccessibleModuleMember = new(
		"AWT153",
		"Module production member not accessible",
		"'{0}' cannot be produced: the member '{2}' exists on the module '{1}' but is not accessible from the generated container; make it public, or internal within a visible assembly",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     An imported <c>[Module]</c> carries a <c>[Scan]</c>. Assembly scanning is a container concern (it
	///     sweeps assemblies relative to the container) and is not collected from modules, so a module-declared
	///     scan would contribute nothing. This is an error rather than a warning so the scan is not silently
	///     dropped. Move it onto the container.
	/// </summary>
	public static readonly DiagnosticDescriptor ScanOnModule = new(
		"AWT154",
		"Scan on module not supported",
		"The imported module '{0}' declares a [Scan], which is not collected from modules and contributes nothing; move the [Scan] onto the container",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     Two different imported modules register the same service key with different implementations at
	///     the same precedence tier (both strongly, or both through open generic templates expanded to the
	///     same closed service), so which one wins single resolution is decided only by [Import] order,
	///     invisible at either module. A warning rather than an error: the graph still resolves (the earlier
	///     import wins, and both implementations stay collection members), but the collision is likely
	///     unintended: override the service on the container, or mark one module's registration
	///     Fallback.Warn or Fallback.Silent. A cross-tier loss (an explicit registration beating another module's expanded
	///     template) is deterministic regardless of import order and stays silent, as does the container
	///     overriding a module: that is the intended override mechanism.
	/// </summary>
	public static readonly DiagnosticDescriptor CrossModuleDuplicate = new(
		"AWT155",
		"Imported modules register the same service",
		"'{0}' is registered by both the imported modules '{1}' and '{2}'; the earlier import '{1}' wins single resolution - override it on the container, or make one registration an overridable default",
		"Awaiten",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <summary>
	///     A generated <c>Root</c> or <c>Scope</c> is disposed synchronously (<c>using</c> or a direct
	///     <c>Dispose()</c> call) although its container owns a service that implements <c>IAsyncDisposable</c>
	///     but not <c>IDisposable</c>. Such a service can only be torn down by <c>DisposeAsync</c>, so the
	///     synchronous drain throws when it reaches one (matching Microsoft.Extensions.DependencyInjection,
	///     rather than leaking it or blocking on its <c>DisposeAsync</c>); <c>await using</c> tears it down
	///     correctly. Reported at the disposal site, not the registration: registering an async-only disposable
	///     is fully supported, and a container that is always disposed with <c>await using</c> is entirely
	///     correct. A warning rather than an error because the throw is conditional at runtime: the drain only
	///     reaches an instance that was actually resolved and tracked on the disposed owner. The check is per
	///     disposed owner: a root-owned instance (a singleton or pre-built instance) always tracks on the
	///     <c>Root</c> (its resolver runs against the root even when first resolved inside a child scope), so a
	///     child <c>Scope</c>'s drain can never reach it, and a container whose only async-only disposables are
	///     root-owned does not warn on a <c>Scope</c> disposal. Reported by <see cref="AwaitenAnalyzer" /> (not
	///     the generator) so a deliberate site can be suppressed in source, and a team can forbid it outright
	///     per project (<c>dotnet_diagnostic.AWT156.severity = error</c>). Only a receiver statically typed as
	///     the generated <c>Root</c>/<c>Scope</c> is recognized: a dispose through <c>IAwaitenScope</c> /
	///     <c>IDisposable</c>, from another assembly, by a host framework, a factory output hiding the
	///     async-only disposable behind a non-disposable declared type, or a <c>Root</c>/<c>Scope</c> of a
	///     container declared in a referenced assembly (whose graph cannot be rebuilt faithfully from the
	///     consuming compilation) all stay invisible to this check; the runtime throw in the generated
	///     synchronous drain remains the backstop there.
	/// </summary>
	public static readonly DiagnosticDescriptor AsyncOnlyDisposal = new(
		"AWT156",
		"Synchronous dispose of a container that requires asynchronous disposal",
		"'{0}' is disposed synchronously, but its container owns '{1}', which implements IAsyncDisposable but not IDisposable; this Dispose() throws when its drain reaches such a service - dispose with DisposeAsync ('await using')",
		"Awaiten",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <summary>
	///     A property marked <c>[Inject(Optional = true)]</c> is <c>required</c>. An optional property is omitted
	///     from the emitted object initializer when its dependency is not registered, which a <c>required</c>
	///     member does not allow (the generated construction would fail with <c>CS9035</c> inside generated code).
	///     Drop <c>required</c>, or drop <c>Optional</c> so the missing dependency is reported as AWT101 instead.
	/// </summary>
	public static readonly DiagnosticDescriptor OptionalPropertyIsRequired = new(
		"AWT157",
		"Optional injected property is required",
		"The property '{0}' on '{1}' is marked [Inject(Optional = true)] but is required; an optional property is omitted from the object initializer when its dependency is not registered, which a required member does not allow (the generated construction would fail with CS9035). Drop 'required', or drop 'Optional'.",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A property marked <c>[Inject(Optional = true)]</c> has only an <c>init</c> accessor. It is still
	///     filled when its dependency is registered, but when the dependency is absent the property is omitted
	///     from the object initializer and left at its default, and an <c>init</c>-only accessor cannot be
	///     assigned afterwards, so it stays default with no fallback. A suppressible warning rather than an error:
	///     the graph is well-defined, but a plain <c>set</c> accessor would let calling code supply a value when
	///     the dependency is not registered.
	/// </summary>
	public static readonly DiagnosticDescriptor OptionalPropertyIsInitOnly = new(
		"AWT158",
		"Optional injected property is init-only",
		"The property '{0}' on '{1}' is marked [Inject(Optional = true)] but is init-only; when its dependency is not registered it is omitted from the object initializer and left at its default, and an init-only accessor cannot be assigned afterwards, so it stays default with no fallback. Give it a plain set accessor so a value can still be assigned after construction when the dependency is not registered.",
		"Awaiten",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <summary>
	///     A synthesized keyed collection (<c>IReadOnlyDictionary&lt;TKey, TService&gt;</c>) cannot be synthesized under
	///     its requested key type: the key type is neither <c>string</c> nor an enum, or it is one of those but the
	///     service's keyed registrations do not all carry a key of that kind. Keyed registrations carry <c>string</c> or
	///     enum constant keys; a dictionary synthesizes only when every keyed registration matches the requested key
	///     type (mixed key kinds have no coherent dictionary).
	/// </summary>
	public static readonly DiagnosticDescriptor UnsupportedKeyedCollectionKey = new(
		"AWT159",
		"Unsupported keyed-collection key type",
		"'{0}' requests a keyed dictionary with key type '{1}', which {2}",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A <c>[FromKey]</c> is applied to a synthesized keyed collection
	///     (<c>IReadOnlyDictionary&lt;string, TService&gt;</c>). The synthesized dictionary resolves <em>every</em>
	///     keyed registration of its service type, so a key selection cannot apply and would otherwise be silently
	///     ignored. An <c>IReadOnlyDictionary</c> service explicitly registered under that key preempts synthesis
	///     and resolves as an ordinary keyed dependency, so it is never reported.
	/// </summary>
	public static readonly DiagnosticDescriptor FromKeyOnKeyedCollection = new(
		"AWT160",
		"[FromKey] on a keyed collection",
		"'{0}' applies [FromKey({1})] to the keyed collection '{2}', which resolves every keyed registration of its service type; remove the [FromKey], or register a dictionary service under that key to take precedence over synthesis",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A <c>[Singleton&lt;T&gt;(Eager = true)]</c> singleton is async-tainted in the strict default: it is
	///     async-initialized (or reaches one through its non-deferred dependencies), so it cannot be constructed
	///     synchronously in the generated root's constructor without handing back an uninitialized instance.
	///     <c>InitializeAsync</c> already warms the async singletons, or opt into
	///     <c>[Container(SyncResolveAfterInit = true)]</c> to allow synchronous eager construction.
	/// </summary>
	public static readonly DiagnosticDescriptor EagerAsyncSingleton = new(
		"AWT161",
		"Eager async-initialized singleton",
		"'{0}' is marked Eager but is async-initialized; it cannot be constructed synchronously at container build time - InitializeAsync already warms the async singletons, or opt into [Container(SyncResolveAfterInit = true)] to allow synchronous eager construction",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A <c>[RequestingType]</c> factory parameter is declared with a type other than
	///     <c>System.Type</c>. The generator fills that slot with the requesting consumer's
	///     <c>typeof(…)</c>, so the parameter must be <c>System.Type</c> to receive it.
	/// </summary>
	public static readonly DiagnosticDescriptor InvalidRequestingType = new(
		"AWT162",
		"Invalid requesting-type parameter",
		"The parameter '{0}' of the factory for '{1}' is marked [RequestingType] but is not of type System.Type; a [RequestingType] parameter receives the requesting consumer's typeof(…), so it must be System.Type",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A factory has both a <c>[RequestingType]</c> parameter and an <c>[Arg]</c> runtime-argument
	///     parameter. A requesting-type factory is built fresh per consumer with the consumer's
	///     <c>typeof(…)</c> supplied at each site, so it is not reached through a <c>Func&lt;TArg…, T&gt;</c>.
	///     The two cannot combine.
	/// </summary>
	public static readonly DiagnosticDescriptor RequestingTypeWithArg = new(
		"AWT163",
		"Requesting-type factory cannot take runtime arguments",
		"The factory for '{0}' has both a [RequestingType] parameter and an [Arg] runtime-argument parameter; a requesting-type factory is built per consumer and cannot also be a parameterized (Func<TArg…, T>) factory, so remove one",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     An <c>OnActivated</c> / <c>OnRelease</c> registration names a member that is not a usable lifecycle
	///     hook on the container: there is no accessible <c>static void</c> method of that name accepting the
	///     registered implementation type.
	/// </summary>
	public static readonly DiagnosticDescriptor InvalidLifecycleHook = new(
		"AWT164",
		"Invalid lifecycle hook",
		"'{0}' cannot use the lifecycle hook '{1}': {2} has no accessible static void method '{1}' accepting '{0}'",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     An <c>OnActivated</c> / <c>OnRelease</c> lifecycle hook is set on a pre-built <c>Instance</c>
	///     registration. The container does not own a pre-built instance - it is constructed, and disposed, by
	///     the caller - so it never runs the hook around it; the hook would be a silent no-op.
	/// </summary>
	public static readonly DiagnosticDescriptor LifecycleHookOnInstance = new(
		"AWT165",
		"Lifecycle hook on a pre-built instance",
		"'{0}' sets an OnActivated/OnRelease lifecycle hook on a pre-built Instance, which the container does not own and never runs the hook around; remove the hook, or register the type for construction instead of as an Instance",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     The same implementation is registered more than once with contradicting per-instance directives -
	///     a different <c>OnActivated</c> or <c>OnRelease</c> lifecycle hook, or one registration opting into
	///     <c>Eager</c> construction that another does not. Coalescing keeps the first registration's directives
	///     and shares a single instance, so a later registration's differing directive would be silently dropped.
	///     A registration that leaves a directive unset expresses no opinion and merges with the winner's rather
	///     than conflicting; only a directive explicitly set to a value the coalesced instance will not use is
	///     reported.
	/// </summary>
	public static readonly DiagnosticDescriptor ConflictingLifecycleDirectives = new(
		"AWT166",
		"Conflicting lifecycle directives",
		"'{0}' is registered with conflicting {1} directives ({2} and {3}); coalescing keeps the first registration's directives, so the other is silently dropped. Declare a single {1} for the implementation.",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A <c>WhenInjectedInto</c> contextual binding never applies, because the named consumer type has no
	///     dependency it can redirect: the binding targets an unkeyed direct constructor parameter or
	///     <c>[Inject]</c> property only, so a <c>[FromKey]</c> dependency, one deferred behind <c>Func</c>/<c>Lazy</c>,
	///     or a collection does not qualify (nor does a consumer that is not registered, or does not take the service
	///     at all). The binding is dead.
	/// </summary>
	public static readonly DiagnosticDescriptor ContextualBindingNeverApplies = new(
		"AWT167",
		"Contextual binding never applies",
		"The registration of '{0}' for '{1}' is never applied: '{1}' has no unkeyed direct '{0}' constructor parameter or [Inject] property to redirect (a [FromKey] dependency, one deferred behind Func/Lazy, or a collection does not qualify)",
		"Awaiten",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <summary>
	///     A registration sets both <c>WhenInjectedInto</c> and <c>Key</c>. A contextual binding is stored under a
	///     synthetic per-consumer key and reached only through that consumer's parameters, so the explicit
	///     <c>Key</c> is silently dropped: the registration could never be selected by a <c>[FromKey]</c>. The two
	///     claim the same resolution slot, so only one can apply.
	/// </summary>
	public static readonly DiagnosticDescriptor ContextualBindingWithKey = new(
		"AWT168",
		"Contextual binding with a key",
		"The registration of '{0}' sets both WhenInjectedInto and Key; a contextual binding is reached only through its consumer, so the Key is silently dropped. Remove one of the two.",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     Two different implementations set <c>WhenInjectedInto</c> for the same service and consumer, so both
	///     claim the one contextual slot for that consumer. Coalescing keeps the first, so the later binding is
	///     silently dropped; the contextual analogue of <see cref="DuplicateKey">AWT117</see>.
	/// </summary>
	public static readonly DiagnosticDescriptor DuplicateContextualBinding = new(
		"AWT169",
		"Duplicate contextual binding",
		"'{0}' has more than one contextual binding for '{1}'; the contextual resolution into '{1}' would be ambiguous",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A <c>[Key]</c> or <c>[FromKey]</c> uses a constant whose type is not a supported key type. A resolution key
	///     must be a <c>string</c>, an <c>enum</c> value, or a <c>typeof(...)</c>; a numeric, <c>char</c> or
	///     <c>bool</c> constant is rejected rather than silently treated as no key.
	/// </summary>
	public static readonly DiagnosticDescriptor UnsupportedKeyType = new(
		"AWT170",
		"Unsupported key type",
		"'{0}' uses an unsupported key type '{1}'; a resolution key must be a string, an enum value, or a typeof(...)",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A closing of an open generic <c>[Decorate]</c>/<c>[Composite]</c> cannot be constructed because its type
	///     arguments violate the decorator's or composite's type-parameter constraints (e.g. a
	///     <c>Logging&lt;T&gt; where T : class</c> behavior applied to <c>IHandler&lt;&gt;</c> when
	///     <c>IHandler&lt;int&gt;</c> is registered). Unlike <see cref="OpenGenericConstraintViolation">AWT126</see>,
	///     a warning rather than an error: the base service still resolves, so that one closing is left
	///     undecorated/unfronted and the remaining closings are decorated/composed as usual.
	/// </summary>
	public static readonly DiagnosticDescriptor DecoratorClosingConstraintViolation = new(
		"AWT171",
		"Decorator or composite skips a constraint-violating closing",
		"'{0}' cannot be constructed from the open generic '{1}': the type argument(s) violate its type-parameter constraints, so that closing is left as-is",
		"Awaiten",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <summary>
	///     A <c>[Scan]</c> matched concrete types assignable to its marker, but its <c>NamePatterns</c>,
	///     <c>NamespacePatterns</c> or <c>Exclude</c> filters removed every one, so the scan registers nothing.
	///     Distinct from AWT138 (the marker itself matched nothing): here the marker matched but the narrowing was
	///     too tight.
	/// </summary>
	public static readonly DiagnosticDescriptor ScanFiltersMatchedNothing = new(
		"AWT172",
		"Assembly scan filters removed every match",
		"Concrete types assignable to '{0}' were found, but the scan's name, namespace and exclude filters removed them all, so nothing is registered",
		"Awaiten",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <summary>
	///     A <c>[Scan]</c> exclusion (an <c>Exclude</c> type, or a <c>!</c>-prefixed <c>NamePatterns</c>/
	///     <c>NamespacePatterns</c> entry) removed no candidate, so it has no effect, most often a type or name left
	///     stale after a rename.
	/// </summary>
	public static readonly DiagnosticDescriptor ScanExclusionNeverMatched = new(
		"AWT173",
		"Scan exclusion never matched",
		"The scan exclusion '{0}' matched no candidate, so it has no effect; it is likely stale after a rename",
		"Awaiten",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <summary>
	///     A <c>[Scan]</c> include pattern matches every candidate (a name pattern of <c>*</c>, or a namespace
	///     pattern of <c>**</c>), so it does not narrow the scan and can be removed.
	/// </summary>
	public static readonly DiagnosticDescriptor ScanPatternMatchesEverything = new(
		"AWT174",
		"Scan pattern matches every candidate",
		"The scan pattern '{0}' matches every candidate, so it does not narrow the scan",
		"Awaiten",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <summary>
	///     A type is declared <c>[ImportService&lt;T&gt;]</c> (drawn from the external provider) yet is also registered
	///     on the container: it is either host-owned or Awaiten-owned, not both. Remove the registration to keep it
	///     external, or drop the <c>[ImportService&lt;T&gt;]</c> to resolve it from the graph.
	/// </summary>
	public static readonly DiagnosticDescriptor ContradictingExternalService = new(
		"AWT175",
		"External service is also registered",
		"'{0}' is declared [ImportService<{0}>] but also registered on the container; a type is either host-owned or Awaiten-owned. Remove the registration or the [ImportService<>] declaration.",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     An <c>[InjectProperty&lt;TImplementation&gt;(name)]</c> on the container names a member that is not a
	///     settable property on <c>TImplementation</c> (or a base type): there is no such member, it is not a
	///     property (a field or method), or the property is read-only (get-only). The container-side analogue of a
	///     typo; a property whose setter merely cannot be reached from the container is <see cref="InjectedPropertyNotSettable">AWT136</see> instead.
	/// </summary>
	public static readonly DiagnosticDescriptor InjectPropertyNotFound = new(
		"AWT177",
		"Injected property not found",
		"'{1}' has no settable property '{0}' for [InjectProperty] to fill; check the name (use nameof), and that it is a property with a set/init accessor rather than a field, method or read-only member",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A type declared <c>[ImportService&lt;T&gt;]</c> is never consumed by any dependency in the graph, so the
	///     declaration is dead - most often a mistyped or stale <c>[ImportService&lt;T&gt;]</c> left after a refactor.
	/// </summary>
	public static readonly DiagnosticDescriptor UnconsumedExternalService = new(
		"AWT176",
		"External service is never consumed",
		"'{0}' is declared [ImportService<{0}>] but no dependency in the container consumes it, so the declaration has no effect. Remove it or add the consuming dependency.",
		"Awaiten",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <summary>
	///     An <c>[InjectProperty&lt;TImplementation&gt;]</c> targets a <c>TImplementation</c> produced by a
	///     <c>Factory</c> or <c>Instance</c> registration. Property injection fills a container-constructed
	///     instance through its object initializer; a factory or pre-built instance is produced whole by its
	///     source, so there is nothing for the container to fill (mirroring how a <c>[Scan]</c>-constructed or
	///     ordinary constructed type is the only one an <c>[Inject]</c> property applies to).
	/// </summary>
	public static readonly DiagnosticDescriptor InjectPropertyOnNonConstructed = new(
		"AWT178",
		"[InjectProperty] on a non-constructed implementation",
		"'{1}' is produced by a Factory or Instance registration, so its property '{0}' cannot be filled by [InjectProperty]; property injection applies only to a container-constructed instance",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     Two <c>[InjectProperty&lt;TImplementation&gt;]</c> entries name the same property of the same
	///     implementation. The property is filled once; the redundant entry is likely a copy-paste and is
	///     reported so a contradicting <c>Optional</c>/<c>Deferred</c>/<c>Key</c> on it is not silently ignored. A
	///     warning rather than an error: the graph is well-defined (the first entry wins), like the other no-op
	///     detections (AWT172-AWT174).
	/// </summary>
	public static readonly DiagnosticDescriptor DuplicateInjectProperty = new(
		"AWT179",
		"Duplicate [InjectProperty] entry",
		"'{1}' has more than one [InjectProperty] entry for the property '{0}'; a property is injected once, so the duplicate is ignored",
		"Awaiten",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <summary>
	///     An <c>[InjectProperty&lt;TImplementation&gt;]</c> targets a <c>TImplementation</c> that has no
	///     container-constructed registration, so the entry is never applied: the type is not registered, or it is
	///     an unexpanded closed generic no consumer required. A warning rather than an error: the container is still
	///     well-defined (the dead entry contributes nothing), the contextual analogue of AWT167. A closed,
	///     container-constructed type is required; an open generic is not matched (its closings are only expanded on
	///     demand), and a Factory/Instance-produced type is AWT178 instead.
	/// </summary>
	public static readonly DiagnosticDescriptor InjectPropertyImplementationNotRegistered = new(
		"AWT180",
		"[InjectProperty] implementation is not registered",
		"'{1}' has no container-constructed registration, so its [InjectProperty] entry for '{0}' is never applied; register '{1}' as a constructed service (a closed, container-constructed type - an unregistered or open-generic type is not matched)",
		"Awaiten",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <summary>
	///     A property is named by both <c>[Inject]</c> and a container-side
	///     <c>[InjectProperty&lt;TImplementation&gt;]</c> entry. The property is filled once, from the <c>[Inject]</c>
	///     attribute, so the entry's <c>Optional</c>/<c>Deferred</c>/<c>Key</c> flags are ignored. A warning rather
	///     than an error: the graph is well-defined (the <c>[Inject]</c> member wins), but the redundant entry - or
	///     its contradicting flags - is likely unintended.
	/// </summary>
	public static readonly DiagnosticDescriptor InjectPropertyAlsoInjectAttribute = new(
		"AWT181",
		"[InjectProperty] duplicates an [Inject] property",
		"the property '{0}' on '{1}' carries [Inject] and also has an [InjectProperty] entry; the [Inject] member wins, so the entry's Optional/Deferred/Key are ignored - remove one",
		"Awaiten",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <summary>
	///     A <c>[Scan(As = ScanAs.MatchingInterface)]</c> matched a concrete type that implements no interface named
	///     <c>I</c> + its own name (the <c>Foo</c>/<c>IFoo</c> convention), so the match contributes no registration.
	///     Implement the convention interface, or add the <c>Self</c> flag (<c>ScanAs.Self | ScanAs.MatchingInterface</c>)
	///     to keep the self registration. A markerless scan does not report this: scanning broadly, a non-conforming
	///     type is expected and simply skipped.
	/// </summary>
	public static readonly DiagnosticDescriptor ScanNoMatchingInterface = new(
		"AWT182",
		"Assembly scan matched a type with no matching interface",
		"'{0}' matched the scan but implements no interface named '{1}', so it is not registered; implement '{1}' or add the ScanAs.Self flag",
		"Awaiten",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <summary>
	///     A markerless <c>[Scan]</c> (the parameterless attribute) is misconfigured: its <c>As</c> includes the
	///     <c>Marker</c> exposure although it names no marker, or it declares no
	///     <c>NamePatterns</c>/<c>NamespacePatterns</c>/<c>InAssembliesOf</c> filter and so would register every
	///     concrete type in scope. Reported as an error, since the scan cannot be expanded as written.
	/// </summary>
	public static readonly DiagnosticDescriptor MarkerlessScanInvalid = new(
		"AWT183",
		"Markerless scan is invalid",
		"The markerless [Scan] {0}",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A markerless <c>[Scan]</c> matched candidate types but registered none of them: either its filters
	///     matched nothing, or (with <c>ScanAs.MatchingInterface</c>) no candidate implements its <c>I</c> + name
	///     interface. Widen the filters or check the naming convention.
	/// </summary>
	public static readonly DiagnosticDescriptor MarkerlessScanRegisteredNothing = new(
		"AWT184",
		"Markerless scan registered nothing",
		"The markerless [Scan] matched no type to register; check its NamePatterns, NamespacePatterns and InAssembliesOf, and (for ScanAs.MatchingInterface) that matches follow the I + name convention",
		"Awaiten",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <summary>
	///     A <c>[Scan]</c>'s <c>As</c> resolved to no <c>ScanAs</c> flag, so the scan would expose its matches
	///     nowhere and register nothing. Usually a <c>&amp;</c> written where <c>|</c> was meant (for example
	///     <c>ScanAs.Self &amp; ScanAs.Marker</c>). Combine the flags with <c>|</c>, or drop <c>As</c> to keep the
	///     <c>Self</c> default.
	/// </summary>
	public static readonly DiagnosticDescriptor ScanExposesNothing = new(
		"AWT185",
		"Scan exposes matches nowhere",
		"The scan's As resolved to no ScanAs flag, so it would register nothing; combine flags with | (not &), or omit As for the Self default",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     An <c>Owned&lt;T&gt;</c>-family relationship (a bare <c>Owned&lt;T&gt;</c>, <c>Func&lt;…, Owned&lt;T&gt;&gt;</c>,
	///     <c>Task&lt;Owned&lt;T&gt;&gt;</c> or <c>Func&lt;…, Task&lt;Owned&lt;T&gt;&gt;&gt;</c>) targets a service
	///     produced by a requesting-type factory (a <c>Factory =</c> method with a <c>[RequestingType]</c> parameter).
	///     Every other <c>Owned&lt;T&gt;</c> form resolves its target into a dedicated throwaway child scope and hands
	///     back a disposal handle, but a requesting-type factory is called per consumer and decides its own disposal,
	///     so it has no owner scope to build into. The combination is unsupported: without this diagnostic the
	///     generator would emit an <c>Owned&lt;T&gt;</c>-typed slot filled with a bare <c>T</c>, a raw <c>CS1503</c>
	///     pointing into generated source. The <c>Lazy&lt;Owned&lt;T&gt;&gt;</c> / <c>Lazy&lt;Task&lt;Owned&lt;T&gt;&gt;&gt;</c>
	///     forms never reach this point: <c>Lazy</c> does not unwrap <c>Owned&lt;T&gt;</c>, so they are the more
	///     specific <see cref="OwnedThroughLazy">AWT121</see> for every service. Mirrors AWT163, which rejects the
	///     analogous <c>[Arg]</c>-plus-<c>[RequestingType]</c> combination. Reported on both constructor parameters
	///     and <c>[Inject]</c> members.
	/// </summary>
	public static readonly DiagnosticDescriptor OwnedOverRequestingTypeFactory = new(
		"AWT186",
		"Owned<T> over a requesting-type factory",
		"'{0}' consumes '{1}' through an Owned<T> relationship, but '{1}' is produced by a requesting-type factory, which has no owner scope; Owned<T> (and its Func/Task forms) is not supported here - consume '{1}' directly, or through Func<T> or Lazy<T>",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A <c>[Scan(As = ScanAs.MatchingInterface)]</c> matched a type that implements several accessible
	///     interfaces named <c>I</c> + its own name (in different namespaces) and no own-namespace winner decides
	///     the tie, so the match registers under each of them. Usually one of them is an incidental same-named
	///     interface: keep a single convention interface, prefer one by declaring it in the type's namespace, or
	///     exclude the type from the scan. Reported only when several interfaces actually register (an
	///     inaccessible candidate is dropped and reported as AWT188 instead) and only for a marker scan, like
	///     the other per-match scan warnings; a match later skipped as unconstructable (AWT141) is not reported.
	/// </summary>
	public static readonly DiagnosticDescriptor ScanAmbiguousMatchingInterfaces = new(
		"AWT187",
		"Scan matched several same-named interfaces",
		"'{0}' implements multiple interfaces named '{1}', so it is registered under each of them; keep a single convention interface or exclude the type from the scan",
		"Awaiten",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <summary>
	///     A scan exposure selected only interfaces the generated container cannot reference: the match implements
	///     its convention (or marker) interface, but that interface is internal to another assembly, so registering
	///     under it would not compile and the match contributes no registration. Distinct from AWT139/AWT182, which
	///     say the interface is not implemented at all. Widen the interface's accessibility (or grant
	///     <c>InternalsVisibleTo</c>), or add the <c>ScanAs.Self</c> flag to keep the self registration.
	/// </summary>
	public static readonly DiagnosticDescriptor ScanInterfaceInaccessible = new(
		"AWT188",
		"Scan matched a type whose interface is inaccessible",
		"'{0}' matched the scan, but its interface '{1}' is not accessible to the generated container, so it is not registered; widen the interface's accessibility or add the ScanAs.Self flag",
		"Awaiten",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <summary>
	///     A lifecycle hook parameter (after the leading instance parameter of an <c>OnActivated</c> /
	///     <c>OnRelease</c> hook) is marked <c>[Arg]</c>. A hook's parameters after the instance are resolved from
	///     the object graph, but a runtime argument flows only through a <c>Func&lt;…&gt;</c> factory into an
	///     <c>[Arg]</c> constructor parameter, and a hook is invoked by the container with no such call site to
	///     supply one. Drop the <c>[Arg]</c>, or resolve the value from the graph instead.
	/// </summary>
	public static readonly DiagnosticDescriptor HookParameterIsArg = new(
		"AWT189",
		"Lifecycle hook parameter cannot be a runtime argument",
		"the parameter '{0}' of the lifecycle hook for '{1}' is marked [Arg], but a hook's parameters are resolved from the graph; runtime arguments are supplied only through a Func<…> factory to constructor parameters, never to a lifecycle hook",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     An <c>OnActivated</c> / <c>OnRelease</c> registration names a method that is overloaded: more than one
	///     accessible <c>static void</c> method of that name accepts the implementation type as its first parameter,
	///     so the container's choice of hook (and of the graph dependencies its remaining parameters resolve) would
	///     be order-dependent. Mirrors <see cref="AmbiguousFactory">AWT112</see> for factory methods.
	/// </summary>
	public static readonly DiagnosticDescriptor AmbiguousLifecycleHook = new(
		"AWT190",
		"Ambiguous lifecycle hook",
		"'{0}' has an ambiguous lifecycle hook: {2} has more than one accessible method '{1}' accepting the instance; the container cannot choose one. Give the hook method a unique name.",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     An <c>OnRelease</c> hook parameter is a <c>Func</c>/<c>Lazy</c> relationship (including their
	///     <c>Task</c> and <c>Owned</c> forms). A release dependency is captured at construction, but a
	///     <c>Func&lt;T&gt;</c> or <c>Lazy&lt;T&gt;</c> captures only a resolver delegate, and the hook runs during
	///     its owner's teardown, when the resolvers refuse (<c>ObjectDisposedException</c>) - the deferred value
	///     could never produce its target. An activation hook may take them freely (it runs while the owner is
	///     alive), so this applies to release hooks only.
	/// </summary>
	public static readonly DiagnosticDescriptor ReleaseHookDeferredParameter = new(
		"AWT191",
		"Release hook parameter cannot defer resolution",
		"the parameter '{0}' of the OnRelease lifecycle hook for '{1}' defers resolution behind a Func/Lazy, but a release hook runs during its owner's teardown, when the container no longer resolves; take the dependency directly, so it is captured at construction",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     <c>SuppressDisposal</c> is set on a pre-built <c>Instance</c> registration. The container does not own a
	///     pre-built instance - it is constructed, and disposed, by the caller - so it never disposes it; suppressing
	///     that disposal is a silent no-op. Mirrors <see cref="LifecycleHookOnInstance">AWT165</see>, which rejects a
	///     lifecycle hook on an <c>Instance</c> for the same reason.
	/// </summary>
	public static readonly DiagnosticDescriptor SuppressDisposalOnInstance = new(
		"AWT192",
		"SuppressDisposal on a pre-built instance",
		"'{0}' sets SuppressDisposal on a pre-built Instance, which the container does not own or dispose, so it has no effect; remove SuppressDisposal, or register the type for construction instead of as an Instance",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);
}
