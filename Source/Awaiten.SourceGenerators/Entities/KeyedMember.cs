namespace Awaiten.SourceGenerators.Entities;

/// <summary>
///     A single keyed member of a keyed-collection-resolvable service: the registration <see cref="Key" />
///     (the <c>[Key]</c> value) and the <see cref="Implementation" /> registered under it. The member is
///     resolved to its instance by simple type name (it is already built, even when it lost the single
///     keyed-resolution slot to an earlier registration under the same key).
/// </summary>
internal readonly record struct KeyedMember(string Key, string Implementation);
