namespace Awaiten.SourceGenerators.Internals;

/// <summary>
///     A single constructor parameter: the underlying service type it resolves (the <c>T</c> inside a
///     <c>Func&lt;T&gt;</c> / <c>Lazy&lt;T&gt;</c>, or the parameter type itself for a direct dependency)
///     and how it is delivered. Relationship-typed parameters are deferred, so they do not contribute
///     graph edges for cycle or captive-dependency analysis.
/// </summary>
internal sealed record ParameterModel(string ServiceType, DependencyKind Kind);
