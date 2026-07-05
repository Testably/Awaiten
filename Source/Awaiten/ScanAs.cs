namespace Awaiten;

/// <summary>
///     How an assembly <see cref="ScanAttribute">scan</see> exposes each matched concrete type: as itself,
///     under the scanned marker interface, or both. Marker registrations make the matches resolvable as a
///     collection of the scanned marker (for example, <c>IEnumerable&lt;IHandler&gt;</c>).
/// </summary>
/// <remarks>
///     The generator reads these as their underlying int off the attribute's <c>TypedConstant</c> and casts
///     to its internal <c>ScanExposure</c> enum, so the numeric values are load-bearing: do not renumber or
///     reorder, and keep them aligned with that mirror.
/// </remarks>
public enum ScanAs
{
	/// <summary>Register each match as its own concrete type (the default).</summary>
	Self = 0,

	/// <summary>
	///     Register each match under the scanned marker interface (and any more-derived interfaces it implements
	///     that are assignable to the marker), never under unrelated interfaces it happens to implement.
	/// </summary>
	Marker = 1,

	/// <summary>Register each match both as its own concrete type and under the scanned marker interface.</summary>
	SelfAndMarker = 2,
}
