using Awaiten.SourceGenerators.Entities;
using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

/// <summary>
///     A single <c>[Composite&lt;TComposite, TService&gt;]</c> registration read from a container: the
///     composed service type name, the service and composite symbols, and the composite's chosen lifetime
///     (defaulting to transient). Collected apart from the lifetime registrations and applied after
///     coalescing (and after decorator chains, so a composite fronts the decorated members) by
///     <c>AwaitenGenerator.BuildComposites</c>, which registers the composite as the public single-dispatch
///     winner while excluding it from collection membership.
/// </summary>
/// <remarks>
///     Like <see cref="RawRegistration" /> this is an intermediate type consumed within a single analysis
///     pass, so it carries the live Roslyn <see cref="Location" /> (not an equatable
///     <c>LocationInfo</c>) and never flows through the generator's incremental cache.
/// </remarks>
internal sealed record CompositeRegistration(
	string Service,
	INamedTypeSymbol ServiceSymbol,
	INamedTypeSymbol Composite,
	Lifetime Lifetime,
	Location? Location);
