namespace Awaiten.SourceGenerators.Internals;

/// <summary>
///     A single constructed instance on a container: one implementation, the implementation's simple
///     name (used to name generated members), its lifetime, the (one or more) service types it is
///     exposed as, its selected constructor's parameters (each a service type plus how it is delivered),
///     whether it needs disposing, and whether it is a reference type (only reference-type cache fields
///     can be marked <c>volatile</c> for the lock-free fast path). Registrations of the same
///     implementation are coalesced into one instance, so a multi-service registration shares a single
///     object. <see cref="Production" /> records how the instance is produced: a constructor (the
///     default), a container <see cref="ProductionMember">method</see> (Factory), or a pre-built
///     container <see cref="ProductionMember">member</see> (Instance); <see cref="FactoryIsStatic" />
///     captures whether a factory method is static (so a nested scope can call it without the container).
/// </summary>
internal sealed record InstanceModel(
	string ImplementationType,
	string Name,
	Lifetime Lifetime,
	EquatableArray<string> ServiceTypes,
	EquatableArray<ParameterModel> ConstructorParameters,
	bool IsDisposable,
	bool IsReferenceType,
	ProductionKind Production = ProductionKind.Constructor,
	string? ProductionMember = null,
	bool FactoryIsStatic = false);