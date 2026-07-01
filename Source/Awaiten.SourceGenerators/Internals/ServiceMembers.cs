namespace Awaiten.SourceGenerators.Internals;

/// <summary>
///     The ordered membership of a collection-resolvable service: the unkeyed service type and every
///     implementation registered under it (deduped, in registration order). Drives
///     <c>IEnumerable&lt;T&gt;</c> / <c>T[]</c> resolution and the public collection dispatch. The
///     implementation names are resolved to their instances by simple type name (they are already built,
///     even when a member lost the single-resolution slot to an earlier registration).
/// </summary>
internal sealed record ServiceMembers(string Service, EquatableArray<string> Implementations);
