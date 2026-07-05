namespace Awaiten.SourceGenerators.Entities;

/// <summary>
///     How a <c>[Scan]</c> exposes its matches. Mirrors the public <c>Awaiten.ScanAs</c> by integer value,
///     read from the attribute's named argument.
/// </summary>
internal enum ScanExposure
{
	Self,
	Marker,
	SelfAndMarker,
}
