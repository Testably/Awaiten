using System;
using System.Diagnostics.CodeAnalysis;

namespace Awaiten;

/// <summary>
///     How an assembly <see cref="ScanAttribute">scan</see> exposes each matched concrete type. The three
///     exposures are independent and combine with <c>|</c>: a match can register as itself, under the scanned
///     marker interface, under the interface named <c>I</c> + its own name, or any combination. Marker
///     registrations make the matches resolvable as a collection of the scanned marker (for example,
///     <c>IEnumerable&lt;IHandler&gt;</c>). The default is <see cref="Self" />.
/// </summary>
/// <remarks>
///     The generator reads these as their underlying int off the attribute's <c>TypedConstant</c> and casts
///     to its internal <c>ScanExposures</c> enum, so the bit values are load-bearing: keep them aligned with that
///     mirror. A value with no bit set exposes nothing and is reported (AWT185).
/// </remarks>
[Flags]
[SuppressMessage("csharpsquid", "S2342:Enumeration types should comply with a naming convention",
	Justification = "ScanAs reads as a fluent fragment at the use site (As = ScanAs.Marker); a plural noun would harm the DSL.")]
public enum ScanAs
{
	/// <summary>Register each match as its own concrete type (the default).</summary>
	Self = 1,

	/// <summary>
	///     Register each match under the scanned marker interface (and any more-derived interfaces it implements
	///     that are assignable to the marker), never under unrelated interfaces it happens to implement. Combine
	///     with <see cref="MatchingInterface" /> to also register under the <c>I</c> + name interface, so a match
	///     joins the marker's collection and stays resolvable by its own interface.
	/// </summary>
	Marker = 2,

	/// <summary>
	///     Register each match under the interface it implements whose name is <c>I</c> + the match's own name
	///     (the <c>Foo</c> implements <c>IFoo</c> convention), never under other interfaces it happens to
	///     implement. A match that implements no such interface is not registered under this exposure; when the scan
	///     names a marker and the match would otherwise register nothing, this is reported (AWT182), while a
	///     markerless scan skips it silently.
	/// </summary>
	MatchingInterface = 4,
}
