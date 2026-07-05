using Awaiten.SourceGenerators.Internals;

namespace Awaiten.SourceGenerators.Entities;

/// <summary>
///     The ordered membership of a keyed-collection-resolvable service: the service (value) type and every
///     keyed implementation registered under it (deduped per key, first-wins, in registration order). Drives
///     <c>IReadOnlyDictionary&lt;string, T&gt;</c> resolution (both the injected dictionary literal and the
///     public by-type dispatch), each member keyed by its <see cref="KeyedMember.Key" />.
/// </summary>
internal sealed record KeyedServiceMembers(string Service, EquatableArray<KeyedMember> Members);
