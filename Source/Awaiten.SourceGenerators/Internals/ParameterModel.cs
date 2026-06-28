namespace Awaiten.SourceGenerators.Internals;

/// <summary>
///     A single constructor parameter: the underlying service type it resolves (the <c>T</c> inside a
///     <c>Func&lt;T&gt;</c> / <c>Lazy&lt;T&gt;</c>, or the parameter type itself for a direct dependency)
///     and how it is delivered. <c>FuncArgTypes</c> holds the leading runtime-argument types of a
///     <c>Func&lt;TArg…, T&gt;</c> relationship (empty otherwise). Relationship-typed and runtime-argument
///     parameters are deferred, so they do not contribute graph edges for cycle or captive-dependency
///     analysis.
/// </summary>
internal sealed record ParameterModel(string ServiceType, DependencyKind Kind, EquatableArray<string> FuncArgTypes = default);
