using System;

namespace Awaiten;

/// <summary>
///     Registers every concrete class assignable to <see cref="AssignableTo" /> (a marker interface or base
///     type) with the chosen <see cref="Lifetime" />, exposed as configured by <see cref="As" />. By default the
///     scan covers the container's own assembly; set <see cref="InAssembliesOf" /> to scan referenced assemblies.
///     When <see cref="AssignableTo" /> is an unbound generic (<c>typeof(IView&lt;&gt;)</c>), a match is a concrete
///     type implementing a closed form of it (<c>View1 : IView&lt;VM1&gt;</c>), registered under that closed
///     interface. This is the equivalent of Autofac's <c>AsClosedTypesOf</c>. Abstract and static classes, and the
///     marker type itself, are skipped.
///     <para>
///         The parameterless constructor is a markerless scan: it names no marker and instead matches by the
///         <see cref="NamePatterns" />/<see cref="NamespacePatterns" />/<see cref="InAssembliesOf" /> filters. Its
///         <see cref="As" /> may not include <see cref="ScanAs.Marker" /> (there is no marker to register under),
///         and requires at least one such filter so it does not sweep every concrete type (AWT183).
///     </para>
/// </summary>
/// <remarks>
///     Scanned registrations are overridable: an explicit registration of the same implementation type takes
///     precedence over the scan for single resolution, so a scan provides bulk defaults that specific registrations
///     refine. Multiple matches of the same service coexist as members of that service's collection. A scan that
///     matches nothing reports AWT138, surfacing a typo or empty marker at build time. A match the container cannot
///     construct is an error by default; set <see cref="SkipUnconstructable" /> to skip it with a warning (AWT141).
///     <para>
///         Scanning reads assembly metadata through Roslyn at compile time, so the generated container stays
///         reflection-free and AOT-clean. No assembly is walked at runtime.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ScanAttribute : Attribute
{
	/// <summary>A markerless scan that matches by the name, namespace and assembly filters instead of a marker.</summary>
	public ScanAttribute() => AssignableTo = null;

	/// <param name="assignableTo">The marker interface or base type to scan for.</param>
	public ScanAttribute(Type assignableTo) => AssignableTo = assignableTo;

	/// <summary>
	///     The marker interface or base type that discovered implementations must be assignable to, or
	///     <see langword="null" /> for a markerless scan (the parameterless constructor).
	/// </summary>
	public Type? AssignableTo { get; }

	/// <summary>The lifetime applied to each discovered implementation. Defaults to <see cref="AwaitenLifetime.Transient" />.</summary>
	public AwaitenLifetime Lifetime { get; set; } = AwaitenLifetime.Transient;

	/// <summary>
	///     How each match is exposed: as itself (the default), under its implemented interfaces, or both.
	///     Registering under the scanned marker interface makes the matches resolvable as a collection, for
	///     example <c>IEnumerable&lt;IHandler&gt;</c>.
	/// </summary>
	public ScanAs As { get; set; } = ScanAs.Self;

	/// <summary>
	///     When set, a match the container cannot construct (a missing dependency or no accessible constructor) is
	///     skipped with a warning (AWT141) instead of failing with AWT101. Off by default. A match that is also
	///     registered explicitly is never skipped.
	/// </summary>
	public bool SkipUnconstructable { get; set; }

	/// <summary>
	///     Widens the scan to the assemblies containing the listed types instead of the container's own assembly.
	///     Each entry names one type whose <see cref="System.Reflection.Assembly" /> is searched, typically a
	///     marker type per referenced project. Matches are discovered from assembly metadata at compile time and
	///     registered in a deterministic order.
	/// </summary>
	public Type[]? InAssembliesOf { get; set; }

	/// <summary>
	///     Includes only candidates whose simple name matches one of these ordinal globs, where <c>*</c> matches any
	///     run of characters (<c>"*Handler"</c>, <c>"Order*"</c>, <c>"*Service*"</c>, or exact <c>"Handler"</c>); a
	///     <c>!</c> prefix excludes instead. A candidate passes when it matches some include (or none are given) and
	///     no exclude. AND-combined with <see cref="NamespacePatterns" /> and <see cref="Exclude" />.
	/// </summary>
	public string[]? NamePatterns { get; set; }

	/// <summary>
	///     Like <see cref="NamePatterns" /> but matched against the containing namespace and segment-aware on
	///     <c>.</c>: <c>*</c> matches one segment and <c>**</c> zero or more, so <c>"MyApp.Services.**"</c> is that
	///     namespace and everything nested beneath it (not the sibling <c>MyApp.ServicesLegacy</c>) and
	///     <c>"**.Tests"</c> is any namespace ending in a <c>Tests</c> segment. A <c>!</c> prefix excludes.
	/// </summary>
	public string[]? NamespacePatterns { get; set; }

	/// <summary>
	///     Removes these types from the scan by exact identity rather than assignability, so an entry survives a
	///     rename and never removes a same-named type elsewhere. An entry that matches no candidate is reported as stale.
	/// </summary>
	public Type[]? Exclude { get; set; }
}

// S2326: the type parameter is the source generator's input. It reads the marker from the attribute's type
// argument via Roslyn, so the body never references it.
#pragma warning disable S2326

/// <summary>
///     Generic form of <see cref="ScanAttribute" />: registers every concrete class assignable to
///     <typeparamref name="TMarker" /> with the chosen <see cref="Lifetime" />, exposed as configured by
///     <see cref="As" />. Equivalent to <c>[Scan(typeof(TMarker))]</c> for a closed marker. An unbound generic
///     marker cannot be written as a type argument, so use the non-generic <see cref="ScanAttribute" /> for the
///     <c>AsClosedTypesOf</c> case.
/// </summary>
/// <remarks>Scanned registrations are overridable and multiple matches coexist as collection members, as for the non-generic <see cref="ScanAttribute" />.</remarks>
/// <typeparam name="TMarker">The marker interface or base type that discovered implementations must be assignable to.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ScanAttribute<TMarker> : Attribute
{
	/// <summary>The lifetime applied to each discovered implementation. Defaults to <see cref="AwaitenLifetime.Transient" />.</summary>
	public AwaitenLifetime Lifetime { get; set; } = AwaitenLifetime.Transient;

	/// <summary>
	///     How each match is exposed: as itself (the default), under its implemented interfaces, or both.
	///     Registering under the scanned marker interface makes the matches resolvable as a collection, for
	///     example <c>IEnumerable&lt;IHandler&gt;</c>.
	/// </summary>
	public ScanAs As { get; set; } = ScanAs.Self;

	/// <summary>
	///     When set, a match the container cannot construct (a missing dependency or no accessible constructor) is
	///     skipped with a warning (AWT141) instead of failing with AWT101. Off by default. A match that is also
	///     registered explicitly is never skipped.
	/// </summary>
	public bool SkipUnconstructable { get; set; }

	/// <summary>
	///     Widens the scan to the assemblies containing the listed types instead of the container's own assembly.
	///     Each entry names one type whose <see cref="System.Reflection.Assembly" /> is searched, typically a
	///     marker type per referenced project. Matches are discovered from assembly metadata at compile time and
	///     registered in a deterministic order.
	/// </summary>
	public Type[]? InAssembliesOf { get; set; }

	/// <summary>
	///     Includes only candidates whose simple name matches one of these ordinal globs, where <c>*</c> matches any
	///     run of characters (<c>"*Handler"</c>, <c>"Order*"</c>, <c>"*Service*"</c>, or exact <c>"Handler"</c>); a
	///     <c>!</c> prefix excludes instead. A candidate passes when it matches some include (or none are given) and
	///     no exclude. AND-combined with <see cref="NamespacePatterns" /> and <see cref="Exclude" />.
	/// </summary>
	public string[]? NamePatterns { get; set; }

	/// <summary>
	///     Like <see cref="NamePatterns" /> but matched against the containing namespace and segment-aware on
	///     <c>.</c>: <c>*</c> matches one segment and <c>**</c> zero or more, so <c>"MyApp.Services.**"</c> is that
	///     namespace and everything nested beneath it (not the sibling <c>MyApp.ServicesLegacy</c>) and
	///     <c>"**.Tests"</c> is any namespace ending in a <c>Tests</c> segment. A <c>!</c> prefix excludes.
	/// </summary>
	public string[]? NamespacePatterns { get; set; }

	/// <summary>
	///     Removes these types from the scan by exact identity rather than assignability, so an entry survives a
	///     rename and never removes a same-named type elsewhere. An entry that matches no candidate is reported as stale.
	/// </summary>
	public Type[]? Exclude { get; set; }
}

#pragma warning restore S2326
