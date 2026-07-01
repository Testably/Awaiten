namespace Awaiten.SourceGenerators.Internals;

/// <summary>
///     The ordered membership of a collection-resolvable service: the element service type, the resolution
///     <see cref="Key" /> the collection is reached under (null for the unkeyed collection, a name for a
///     <c>[FromKey]</c> collection), and every implementation registered under that (type, key) - deduped, in
///     registration order. Drives <c>IEnumerable&lt;T&gt;</c> / <c>T[]</c> resolution; an unkeyed collection
///     also drives the public collection dispatch (a keyed collection is reached only by <c>[FromKey]</c>
///     injection, never a by-type resolution). The implementation names are resolved to their instances by
///     simple type name (they are already built, even when a member lost the single-resolution slot to an
///     earlier registration).
/// </summary>
internal sealed record ServiceMembers(string Service, string? Key, EquatableArray<string> Implementations);
