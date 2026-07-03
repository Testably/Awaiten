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
///     the instance is constructed and cached, so it contributes no <em>cycle</em> edge - which is what lets
///     it break a mutual constructor cycle - but it still participates in captive and async-taint analysis
///     (its assignment captures the target for the owner's lifetime and awaits an async-initialized target).
/// </summary>
internal sealed record MemberModel(string MemberName, ParameterModel Dependency, bool Deferred = false);
