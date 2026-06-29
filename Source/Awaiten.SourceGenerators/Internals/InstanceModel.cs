using System.Linq;

namespace Awaiten.SourceGenerators.Internals;

/// <summary>
///     A single constructed instance on a container: one implementation, the implementation's simple
///     name (used to name generated members), its lifetime, the (one or more) service keys it is
///     exposed as (a service type plus an optional resolution key), its selected constructor's
///     parameters (each a service type plus how it is delivered),
///     whether it needs disposing, and whether it is a reference type (only reference-type cache fields
///     can be marked <c>volatile</c> for the lock-free fast path). Registrations of the same
///     implementation are coalesced into one instance, so a multi-service registration shares a single
///     object. <see cref="Production" /> records how the instance is produced: a constructor (the
///     default), a container <see cref="ProductionMember">method</see> (Factory), or a pre-built
///     container <see cref="ProductionMember">member</see> (Instance). The container is a static class, so
///     a factory method or instance member is always reached by simple name.
///     <see cref="IsAsyncInitializable" /> is set when the constructed/factory-produced type implements
///     <c>IAsyncInitializable</c> (so it must be awaited once after construction);
///     <see cref="IsAsyncTainted" /> additionally covers an instance that only reaches one through its
///     non-deferred dependencies, so it too must be resolved asynchronously.
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
	bool IsAsyncTainted = false)
{
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
}
