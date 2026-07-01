using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

/// <summary>
///     A single <c>[Decorate&lt;TDecorator, TService&gt;]</c> registration read from a container: the
///     decorated service type name, the service and decorator symbols, the requested chain
///     <see cref="Order" />, and the declaration index used to break ties between equal orders (so
///     decorators chain in declaration order by default). Collected apart from the lifetime registrations
///     and expanded after coalescing by <c>AwaitenGenerator.BuildDecoratorChains</c> into synthetic-keyed
///     chain links.
/// </summary>
/// <remarks>
///     Like <see cref="RawRegistration" /> this is an intermediate type consumed within a single analysis
///     pass, so it carries the live Roslyn <see cref="Location" /> (not an equatable
///     <c>LocationInfo</c>) and never flows through the generator's incremental cache.
/// </remarks>
internal sealed record DecorateRegistration(
	string Service,
	INamedTypeSymbol ServiceSymbol,
	INamedTypeSymbol Decorator,
	int Order,
	int DeclarationOrder,
	Location? Location);
