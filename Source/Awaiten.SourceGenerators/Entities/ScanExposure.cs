using System;

namespace Awaiten.SourceGenerators.Entities;

/// <summary>
///     How a <c>[Scan]</c> exposes its matches. Mirrors the public <c>Awaiten.ScanAs</c> by bit value, read from
///     the attribute's named argument, so these values must stay aligned with it. The three exposures are
///     independent flags and combine.
/// </summary>
[Flags]
internal enum ScanExposure
{
	Self = 1,
	Marker = 2,
	MatchingInterface = 4,
}
