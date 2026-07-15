using System;

namespace Awaiten.SourceGenerators.Entities;

/// <summary>
///     How a <c>[Scan]</c> exposes its matches. Mirrors the public <c>Awaiten.ScanAs</c> by bit value, read from
///     the attribute's named argument, so these values must stay aligned with it. The three exposures are
///     independent flags and combine. <see cref="All" /> is generator-side only (not mirrored in
///     <c>ScanAs</c>): every recognized bit, so an <c>As</c> that names no exposure is detected (AWT185).
/// </summary>
[Flags]
internal enum ScanExposures
{
	Self = 1,
	Marker = 2,
	MatchingInterface = 4,
	All = Self | Marker | MatchingInterface,
}
