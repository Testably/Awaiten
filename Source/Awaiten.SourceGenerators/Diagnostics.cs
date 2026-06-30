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
	///     the produced instance needs initialization - the hidden <c>InitializeAsync</c> never runs and the
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
	///     A <c>Factory</c> registration names a member that is not a usable factory method on the
	///     container (it is missing, is not a method, or does not return the registered service type).
	/// </summary>
	public static readonly DiagnosticDescriptor InvalidFactory = new(
		"AWT108",
		"Invalid factory",
		"'{0}' cannot be produced: the container has no accessible method '{1}' returning '{0}'",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     An <c>Instance</c> registration names a member that is not a usable instance member on the
	///     container (it is missing, is not a field or property, or is not assignable to the service type).
	/// </summary>
	public static readonly DiagnosticDescriptor InvalidInstance = new(
		"AWT109",
		"Invalid instance member",
		"'{0}' cannot be exposed: the container has no accessible field or property '{1}' of type '{0}'",
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
	///     constructor, factory or instance - or the same kind naming a different member); coalescing into
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
		"'{0}' has an ambiguous factory: the container has more than one accessible method '{1}' returning '{0}'. Give the factory method a unique name.",
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
	///     parameterized service) whose construction tracks a fresh disposable on the root - the produced
	///     service is itself disposable, or it transitively rebuilds a disposable transient. Each call to that
	///     factory builds and re-tracks those disposables on the container's root, so they accumulate for its
	///     entire lifetime - an unbounded leak. The leak-free remedy is the <c>{2}</c> message argument, since it
	///     differs by relationship: a synchronous <c>Func&lt;…&gt;</c> is redirected to a
	///     <c>Func&lt;…, Owned&lt;T&gt;&gt;</c> disposal handle (draining into a throwaway scope), while an
	///     asynchronous <c>Func&lt;…, Task&lt;T&gt;&gt;</c> cannot use <c>Owned&lt;T&gt;</c> - a synchronous handle
	///     that cannot await initialization (AWT119) - so it is pointed at an explicitly scoped resolution instead.
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
	///     <c>#pragma warning disable</c>, <c>&lt;NoWarn&gt;</c> or an editorconfig severity override - the only
	///     way to opt out is to set <c>LifetimeSafety.Loose</c> on the <c>[Container]</c>, which switches back
	///     to the suppressible <see cref="RootAccumulatingFactory" />. This is what makes the root-accumulation
	///     leak structurally impossible under strict lifetime safety rather than merely warned-about.
	/// </summary>
	/// <remarks>
	///     Its declared default severity is <see cref="DiagnosticSeverity.Warning" /> - identical to
	///     <see cref="RootAccumulatingFactory" /> - so that release tracking sees a single, consistent AWT118;
	///     the analyzer raises it to an error per report. <see cref="WellKnownDiagnosticTags.NotConfigurable" />,
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
}
