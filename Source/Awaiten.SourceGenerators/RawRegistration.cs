using Awaiten.SourceGenerators.Internals;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

/// <summary>
///     A single lifetime registration read from a <c>[Singleton]</c>/<c>[Transient]</c>/<c>[Scoped]</c>
///     attribute on a container: the service and implementation type names, the lifetime, the
///     implementation symbol, and the attribute's source location.
/// </summary>
/// <remarks>
///     <see cref="Location" /> is the live Roslyn location (with its syntax tree), not an equatable
///     <see cref="LocationInfo" />: an analyzer needs the syntax tree for <c>#pragma</c> suppression to
///     apply. This type is intermediate (consumed within a single analysis pass), so it never flows
///     through the generator's incremental cache and does not need to be equatable.
/// </remarks>
internal sealed record RawRegistration(
	string ServiceType,
	string ImplementationType,
	Lifetime Lifetime,
	INamedTypeSymbol Implementation,
	Location? Location);
