namespace Awaiten.SourceGenerators.Entities;

/// <summary>
///     A single entry of a runtime-type dispatch dictionary: the closed generic <see cref="Service" /> it resolves
///     (e.g. <c>IHandler&lt;OrderPlaced&gt;</c>) and the fully-qualified type argument it is keyed by
///     (<see cref="KeyType" />, emitted as <c>typeof(…)</c>).
/// </summary>
internal readonly record struct DispatchMember(string KeyType, string Service);
