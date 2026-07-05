namespace Awaiten;

/// <summary>
///     How an assembly <see cref="ScanAttribute">scan</see> exposes each matched concrete type: as itself,
///     under the scanned marker interface, or both. Marker registrations make the matches resolvable as a
///     collection of the scanned marker (for example, <c>IEnumerable&lt;IHandler&gt;</c>).
/// </summary>
public enum ScanAs
{
	/// <summary>Register each match as its own concrete type (the default).</summary>
	Self,

	/// <summary>
	///     Register each match under the scanned marker interface (and any more-derived interfaces it implements
	///     that are assignable to the marker), never under unrelated interfaces it happens to implement.
	/// </summary>
	Marker,

	/// <summary>Register each match both as its own concrete type and under the scanned marker interface.</summary>
	SelfAndMarker,
}
