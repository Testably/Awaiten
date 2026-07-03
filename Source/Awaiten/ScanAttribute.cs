using System;

namespace Awaiten;

/// <summary>
///     Registers every concrete class in the container's own assembly that is assignable to
///     <see cref="AssignableTo" /> (a marker interface or base type) with the chosen <see cref="Lifetime" />,
///     exposed as configured by <see cref="As" />. Abstract and static classes, and the marker type itself, are
///     skipped.
/// </summary>
/// <remarks>
///     Scanned registrations are overridable: an explicit registration of the same implementation type (via
///     <c>[Transient&lt;T&gt;]</c> and friends) takes precedence over the scan for single resolution, so a scan
///     can provide bulk defaults that specific registrations refine. Multiple matches of the same service coexist
///     as members of that service's collection - scanning is meant to collect, so siblings do not displace each
///     other. A scan that matches no concrete type reports AWT138, so a typo or an empty marker surfaces at build
///     time.
///     <para>
///         Scanning reads the container's own assembly through Roslyn symbols at compile time, so the generated
///         container stays reflection-free and AOT-clean. Referenced assemblies are not walked.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ScanAttribute : Attribute
{
	/// <summary>
	///     Initializes a new instance of the <see cref="ScanAttribute" /> class.
	/// </summary>
	/// <param name="assignableTo">The marker interface or base type to scan for.</param>
	public ScanAttribute(Type assignableTo) => AssignableTo = assignableTo;

	/// <summary>
	///     The marker interface or base type that discovered implementations must be assignable to.
	/// </summary>
	public Type AssignableTo { get; }

	/// <summary>
	///     The lifetime applied to each discovered implementation. Defaults to
	///     <see cref="AwaitenLifetime.Transient" />.
	/// </summary>
	public AwaitenLifetime Lifetime { get; set; } = AwaitenLifetime.Transient;

	/// <summary>
	///     How each match is exposed: as itself (the default), under its implemented interfaces, or both.
	///     Registering under the scanned marker interface makes the matches resolvable as a collection (for
	///     example, <c>IEnumerable&lt;IHandler&gt;</c>).
	/// </summary>
	public ScanAs As { get; set; } = ScanAs.Self;
}
