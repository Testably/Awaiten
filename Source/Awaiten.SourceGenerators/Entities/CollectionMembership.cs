using System.Collections.Generic;

namespace Awaiten.SourceGenerators.Entities;

/// <summary>
///     The built-instance indices of every collection-resolvable service's members, in both membership kinds:
///     <see cref="Collections" /> for the plain (element type, key) collections and <see cref="Keyed" /> for the
///     keyed collections (<c>IReadOnlyDictionary&lt;string, T&gt;</c>), grouped by service (value) type. Composed
///     once per walk (see <c>AwaitenGenerator.MembershipIndices</c>) so the transitive-disposable analysis
///     (AWT118 / strict withholding) and the dispatch emission follow both membership kinds through one handle.
/// </summary>
internal readonly record struct CollectionMembership(
	Dictionary<ServiceKey, List<int>> Collections,
	Dictionary<string, List<int>> Keyed);
