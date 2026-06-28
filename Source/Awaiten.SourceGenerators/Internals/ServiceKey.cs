namespace Awaiten.SourceGenerators.Internals;

/// <summary>
///     The resolution key of a registration: the service type plus an optional <c>Key</c>. Keyed
///     registrations (a non-null <see cref="Key" />) let several implementations share one service
///     type, distinguished at the injection site by <c>[FromKey]</c>. The unkeyed registration of a
///     service type is <c>(Service, null)</c>; a keyed one is <c>(Service, "name")</c>, so the two
///     never collide in the resolution maps.
/// </summary>
internal readonly record struct ServiceKey(string Service, string? Key);
