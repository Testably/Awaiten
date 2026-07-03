namespace Awaiten;

/// <summary>
///     How an assembly <see cref="ScanAttribute">scan</see> exposes each matched concrete type: as itself,
///     under the interfaces it implements, or both. Interface registrations make the matches resolvable as a
///     collection of the scanned marker (for example, <c>IEnumerable&lt;IHandler&gt;</c>).
/// </summary>
public enum ScanAs
{
	/// <summary>Register each match as its own concrete type (the default).</summary>
	Self,

	/// <summary>
	///     Register each match under the interfaces it implements that are assignable to the scanned marker
	///     type (so a marker interface registers every match under itself).
	/// </summary>
	ImplementedInterfaces,

	/// <summary>Register each match both as its own concrete type and under its implemented interfaces.</summary>
	SelfAndImplementedInterfaces,
}
