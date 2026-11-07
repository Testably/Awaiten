using System.Collections.Generic;
using System.Linq;
using Awaiten.SourceGenerators.Internals;

namespace Awaiten.SourceGenerators.Entities;

/// <summary>
///     A single constructed instance on a container: one implementation, its lifetime, the service keys it is
///     exposed as, its selected constructor's parameters, its <c>[Inject]</c> properties, and disposal/async flags.
///     Registrations of the same implementation are coalesced into one instance, so a multi-service registration
///     shares a single object. <see cref="Production" /> records how it is produced: a constructor,
///     a container Factory method, or a pre-built Instance member. Four distinct taint sources each force
///     asynchronous resolution: <see cref="AsyncInitializers" /> (the <c>[AsyncInitializer&lt;T&gt;]</c> conventions of
///     its declaring scope it matches, awaited after <see cref="OnActivated" /> in that order), <see cref="IsAsyncFactory" /> (produced by an async factory),
///     <see cref="IsAsyncActivation" /> (its <c>OnActivated</c> hook returns <c>Task</c> or <c>ValueTask</c>), and
///     <see cref="IsAsyncTainted" /> (reaches one through a non-deferred dependency).
///     <see cref="RuntimeDisposalCheck" /> is set for a factory whose declared return type is not
///     <c>IDisposable</c> but could produce one at runtime, so the resolver tracks it behind a runtime type test.
///     <see cref="Eager" /> marks a singleton built at container build time rather than lazily on first resolve.
///     <see cref="OnActivated" /> / <see cref="OnRelease" /> are the resolved names of the container's
///     <c>static void M(TImplementation, …)</c> lifecycle hooks. The activation hook runs once the instance is
///     constructed; the release hook is queued at construction and run when the owning Root/Scope is disposed,
///     before the instance's own disposal. Each hook's first parameter is the instance; any parameters after it
///     are graph dependencies, carried as <see cref="ActivationParameters" /> / <see cref="ReleaseParameters" />
///     and resolved exactly like a constructor parameter (an activation dependency inline at the call, a release
///     dependency captured by value into the queued closure). <see cref="SuppressDisposal" /> opts the instance
///     out of the container's built-in disposal entirely (teardown is left to an <see cref="OnRelease" /> hook or
///     an owner outside the container), so a suppressed instance is never tracked for teardown.
///     <see cref="IsAsyncRelease" /> marks a release hook returning <c>Task</c> or <c>ValueTask</c>, which only
///     <c>DisposeAsync</c> can await; it does not taint the instance, since it runs at teardown.
///     <see cref="AsyncReleaseHookName" /> is the declared name of such a hook, for the guidance a synchronous
///     <c>Dispose</c> throws over it; it is null for a generated wrapper, whose name means nothing to the user.
///     <see cref="OutermostDecorators" /> holds the instance indices of the outermost decorator chain links wrapping
///     this instance, one per decorated slot; an <see cref="Eager" /> instance is activated through them, so it is
///     built as its consumers see it.
///     <see cref="PrivateServices" /> are the service types that are private or protected nested types, which the
///     generated <c>Root</c> cannot hold in an <c>internal</c> field, so their typed async resolution is not cached.
///     <see cref="ResolverType" /> is the accessible type the resolvers return for an implementation private to the
///     container, which converts to all of its services (see <see cref="SurfaceType" />).
/// </summary>
internal sealed record InstanceModel(
	string ImplementationType,
	string Name,
	Lifetime Lifetime,
	EquatableArray<ServiceKey> Services,
	EquatableArray<ParameterModel> ConstructorParameters,
	bool IsDisposable,
	bool IsReferenceType,
	ProductionKind Production = ProductionKind.Constructor,
	string? ProductionMember = null,
	EquatableArray<AsyncInitializerModel> AsyncInitializers = default,
	bool IsAsyncTainted = false,
	bool IsAsyncFactory = false,
	bool RuntimeDisposalCheck = false,
	bool IsAsyncDisposable = false,
	string? EmitType = null,
	EquatableArray<MemberModel> InjectedMembers = default,
	bool Eager = false,
	string? OnActivated = null,
	string? OnRelease = null,
	EquatableArray<ParameterModel> ActivationParameters = default,
	EquatableArray<ParameterModel> ReleaseParameters = default,
	bool SuppressDisposal = false,
	EquatableArray<int> OutermostDecorators = default,
	EquatableArray<string> PrivateServices = default,
	string? ResolverType = null,
	bool IsAsyncActivation = false,
	bool IsAsyncRelease = false,
	string? AsyncReleaseHookName = null,
	bool Unlisted = false)
{
	/// <summary>
	///     The services the container offers for resolution: none for an <see cref="Unlisted" /> instance, the generated
	///     handle of a service a module of another assembly registers under an internal type, which only dependencies
	///     reach, so it gets no resolver interface, dispatch entry or advertised registration.
	/// </summary>
	public ServiceKey[] ListedServices => Unlisted ? [] : Services.AsArray();

	/// <summary>
	///     The type the generated resolvers return and the async cache holds: <see cref="ResolverType" /> for an
	///     implementation private to the container, which only the resolver bodies may name, else
	///     <see cref="ConstructedType" />.
	/// </summary>
	public string SurfaceType => ResolverType ?? ConstructedType;

	/// <summary>
	///     The concrete type to construct and to use for cache fields and, unless <see cref="ResolverType" /> is set,
	///     resolver return types (see <see cref="SurfaceType" />). Normally the same
	///     as <see cref="ImplementationType" />, but a decorator chain link reuses one decorator type across several
	///     instances, so it carries a synthetic <see cref="ImplementationType" /> identity while
	///     <see cref="EmitType" /> holds the real type. Ordinary registrations (<see cref="EmitType" /> null) are unaffected.
	/// </summary>
	public string ConstructedType => EmitType ?? ImplementationType;

	/// <summary>
	///     Whether the container owns this instance for disposal (its declared type implements <c>IDisposable</c> or
	///     <c>IAsyncDisposable</c>), so it is tracked for teardown. The drain selects the right disposal at runtime.
	///     <see cref="SuppressDisposal" /> opts out: a suppressed instance is not tracked and reads here as
	///     non-disposable. Lifetime-safety analysis (strict withholding / AWT118) does not read this alone: an
	///     <see cref="OnRelease" /> hook's queued closure retains the instance on the owner until teardown, so a
	///     suppressed instance with a release hook still counts as accumulating there.
	/// </summary>
	public bool NeedsDisposal => !SuppressDisposal && (IsDisposable || IsAsyncDisposable);

	/// <summary>
	///     Whether this instance is itself an async-taint source (not merely tainted through a dependency): it is
	///     initialized by an async initializer convention, produced by an async factory, or activated by an awaited
	///     <c>OnActivated</c> hook. Reachable only by awaiting, so a synchronous relationship over it is AWT119 rather
	///     than a transitive AWT120.
	/// </summary>
	public bool IsAsyncSource => AsyncInitializers.Count > 0 || IsAsyncFactory || IsAsyncActivation;

	/// <summary>
	///     The ordered runtime-argument types of this instance: the service types of its <c>[Arg]</c>-marked
	///     constructor parameters, in declaration order. These are supplied at resolve time through a
	///     <c>Func&lt;TArg…, T&gt;</c> rather than from the object graph.
	/// </summary>
	public string[] ArgTypes() => ConstructorParameters.AsArray()
		.Where(p => p.Kind == DependencyKind.Arg)
		.Select(p => p.ServiceType)
		.ToArray();

	/// <summary>
	///     Whether this instance has any <c>[Arg]</c>-marked parameters and so is parameterized: built fresh
	///     from its runtime arguments on every request and reachable only through its
	///     <c>Func&lt;TArg…, T&gt;</c> factory.
	/// </summary>
	public bool IsParameterized => ArgTypes().Length > 0;

	/// <summary>
	///     Whether this instance is produced by a requesting-type factory: a <c>Factory =</c> method with a
	///     <c>[RequestingType]</c> parameter (<see cref="DependencyKind.RequestingType" />). Its resolver
	///     embeds the consumer's <c>typeof(…)</c> per call, so unlike an ordinary registration it cannot
	///     be lowered to a shared cached resolver: it is built fresh on every invocation (the declared
	///     lifetime is ignored for caching) and, like a parameterized service, pruned from collection
	///     membership.
	/// </summary>
	public bool IsRequestingTypeFactory => ConstructorParameters.AsArray().Any(p => p.Kind == DependencyKind.RequestingType);

	/// <summary>Whether this instance runs a lifecycle hook when the owning Root/Scope is disposed.</summary>
	public bool HasReleaseHook => OnRelease is not null;

	/// <summary>
	///     The lifecycle hooks' graph-resolved parameters (those after the leading instance parameter of the
	///     <c>OnActivated</c> and <c>OnRelease</c> hooks, and those of the <see cref="AsyncInitializers" />), classified
	///     exactly like constructor parameters and resolved eagerly at construction (the activation and initializer
	///     dependencies inline at the call, the release dependency captured by value). Every dependency-walking pass that
	///     visits <see cref="ConstructorParameters" /> must also visit these, or a hook dependency escapes
	///     cycle/captive/async/argument analysis and the emitted infrastructure it needs. The common case (no hook
	///     parameters, or only one hook contributing any) returns a backing array without allocating.
	/// </summary>
	public ParameterModel[] HookParameters()
	{
		ParameterModel[] activation = ActivationParameters.AsArray();
		ParameterModel[] release = ReleaseParameters.AsArray();
		AsyncInitializerModel[] initializers = AsyncInitializers.AsArray();
		if (initializers.Length == 0)
		{
			if (release.Length == 0)
			{
				return activation;
			}

			if (activation.Length == 0)
			{
				return release;
			}
		}

		return activation
			.Concat(initializers.SelectMany(initializer => initializer.Parameters.AsArray()))
			.Concat(release)
			.ToArray();
	}

	/// <summary>
	///     Every graph dependency this instance resolves: its constructor/factory parameters, its injected
	///     <c>[Inject]</c> member dependencies and its lifecycle hook parameters, in that order. For the
	///     dependency-walking passes that treat all three groups alike; a pass that distinguishes them (deferred
	///     members in the cycle graph, say) enumerates the groups itself.
	/// </summary>
	public IEnumerable<ParameterModel> WalkedDependencies()
		=> ConstructorParameters.AsArray()
			.Concat(InjectedMembers.AsArray().Select(member => member.Dependency))
			.Concat(HookParameters());
}
