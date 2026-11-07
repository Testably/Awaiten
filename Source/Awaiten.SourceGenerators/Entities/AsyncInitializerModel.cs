using Awaiten.SourceGenerators.Internals;

namespace Awaiten.SourceGenerators.Entities;

/// <summary>
///     One <c>[AsyncInitializer&lt;T&gt;(method)]</c> convention that initializes an instance: the fully qualified
///     convention <see cref="Type" /> the instance is cast to, the <see cref="Method" /> awaited through it, and the
///     method's <see cref="Parameters" />, classified like those of an asynchronous <c>OnActivated</c> hook (a
///     <c>CancellationToken</c> receives the resolve-time token, every other parameter is a graph dependency).
/// </summary>
internal sealed record AsyncInitializerModel(string Type, string Method, EquatableArray<ParameterModel> Parameters);
