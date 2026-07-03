namespace Awaiten.SourceGenerators.Internals;

/// <summary>
///     A single injected member: a property marked <c>[Inject]</c>, assigned through an object
///     initializer after construction (property injection is opt-in - a plain <c>required</c> property
///     is not auto-injected). <see cref="MemberName" /> is the property to assign;
///     <see cref="Dependency" /> is how it resolves, classified exactly like a constructor parameter
///     (direct, keyed, a relationship type or a collection). A property edge is treated exactly like the
///     corresponding constructor edge for cycle, captive and async-taint analysis. An injected member
///     never carries a runtime argument (<c>[Arg]</c> on a property is <c>AWT137</c>). A
///     <see cref="Deferred" /> member (<c>[Inject(Deferred = true)]</c>) is instead assigned <em>after</em>
///     the instance is constructed and cached and contributes no graph edge at all - excluded from cycle,
///     captive and async-taint analysis like a relationship type - so it can break a mutual constructor cycle.
/// </summary>
internal sealed record MemberModel(string MemberName, ParameterModel Dependency, bool Deferred = false);
