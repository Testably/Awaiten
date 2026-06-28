namespace Awaiten.SourceGenerators.Internals;

/// <summary>
///     A single constructor parameter: the underlying service type it resolves (the <c>T</c> inside a
///     <c>Func&lt;T&gt;</c> / <c>Lazy&lt;T&gt;</c>, or the parameter type itself for a direct dependency)
///     and how it is delivered. <c>FuncArgTypes</c> holds the leading runtime-argument types of a
///     <c>Func&lt;TArg…, T&gt;</c> relationship (empty otherwise). <c>ProducesOwned</c> marks a
///     <c>Func&lt;…, Owned&lt;T&gt;&gt;</c> relationship whose produced value is wrapped in an
///     <c>Owned&lt;T&gt;</c> disposal handle (a bare <c>Owned&lt;T&gt;</c> uses <see cref="DependencyKind.Owned" />
///     instead). <c>Location</c> is the parameter's own source location, so a diagnostic about how it
///     consumes its dependency can point at the parameter rather than the registration. Relationship-typed,
///     runtime-argument and owned parameters are deferred, so they do not contribute graph edges for cycle
///     or captive-dependency analysis.
/// </summary>
internal sealed record ParameterModel(
	string ServiceType,
	DependencyKind Kind,
	EquatableArray<string> FuncArgTypes = default,
	LocationInfo? Location = null,
	bool ProducesOwned = false);
