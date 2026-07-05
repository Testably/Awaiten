namespace Awaiten.SourceGenerators.Entities;

/// <summary>
///     How a <c>[Scan]</c> exposes its matches. Mirrors the public <c>Awaiten.ScanAs</c> by integer value,
///     read from the attribute's named argument, so these values must stay aligned with it.
/// </summary>
internal enum ScanExposure
{
	Self = 0,
	Marker = 1,
	SelfAndMarker = 2,
}
