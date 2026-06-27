namespace Awaiten.SourceGenerators.Internals;

/// <summary>
///     How an instance is produced. <see cref="Constructor" /> calls a selected constructor;
///     <see cref="Factory" /> calls a method on the container; <see cref="Instance" /> hands back a
///     pre-built member of the container (which the container neither constructs nor disposes).
/// </summary>
internal enum ProductionKind
{
	Constructor,
	Factory,
	Instance,
}
