using System.Linq;
using Awaiten.SourceGenerators.Internals;

namespace Awaiten.SourceGenerators.Entities;

/// <summary>
///     A single constructed instance on a container: one implementation, its lifetime, the service keys it is
///     exposed as, its selected constructor's parameters, its <c>[Inject]</c> properties, and disposal/async flags.
///     Registrations of the same implementation are coalesced into one instance, so a multi-service registration
///     shares a single object. <see cref="Production" /> records how it is produced: a constructor,
///     a container Factory method, or a pre-built Instance member. The three async flags are distinct taint
///     sources, any of which forces asynchronous resolution: <see cref="IsAsyncInitializable" /> (implements
///     <c>IAsyncInitializable</c>), <see cref="IsAsyncFactory" /> (produced by an async factory), and
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
///     dependency captured by value into the queued closure).
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
	bool IsAsyncInitializable = false,
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
	EquatableArray<ParameterModel> ReleaseParameters = default)
{
	/// <summary>
	///     The concrete type to construct and to use for cache fields and resolver return types. Normally the same
	///     as <see cref="ImplementationType" />, but a decorator chain link reuses one decorator type across several
	///     instances, so it carries a synthetic <see cref="ImplementationType" /> identity while
	///     <see cref="EmitType" /> holds the real type. Ordinary registrations (<see cref="EmitType" /> null) are unaffected.
	/// </summary>
	public string ConstructedType => EmitType ?? ImplementationType;

	/// <summary>
	///     Whether the container owns this instance for disposal (its declared type implements <c>IDisposable</c> or
	///     <c>IAsyncDisposable</c>), so it is tracked for teardown. The drain selects the right disposal at runtime.
	/// </summary>
	public bool NeedsDisposal => IsDisposable || IsAsyncDisposable;

	/// <summary>
	///     Whether this instance is itself an async-taint source (not merely tainted through a dependency): it is
	///     <c>IAsyncInitializable</c> or produced by an async factory. Reachable only by awaiting, so a synchronous
	///     relationship over it is AWT119 rather than a transitive AWT120.
	/// </summary>
	public bool IsAsyncSource => IsAsyncInitializable || IsAsyncFactory;

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
}
