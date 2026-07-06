using Awaiten.SourceGenerators.Internals;

namespace Awaiten.SourceGenerators.Entities;

/// <summary>
///     The ordered membership of a keyed-collection-resolvable service: the service (value) type and every
///     keyed implementation registered under it (deduped per key, first-wins, in registration order). Drives
///     <c>IReadOnlyDictionary&lt;TKey, T&gt;</c> resolution (both the injected dictionary literal and the
///     public by-type dispatch), each member keyed by its <see cref="KeyedMember.Key" />. <see cref="KeyType" /> is
///     the C# key type the by-type dictionary is synthesized under (the <c>string</c> keyword when every member has a
///     string key, or a fully-qualified enum type when every member is a constant of that one enum), or
///     <see langword="null" /> when the members mix key kinds and no coherent by-type dictionary exists.
/// </summary>
internal sealed record KeyedServiceMembers(string Service, EquatableArray<KeyedMember> Members, string? KeyType);
