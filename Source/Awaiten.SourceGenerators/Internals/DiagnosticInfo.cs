using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators.Internals;

/// <summary>
///     An equatable description of a diagnostic to report, deferred until the source-output stage.
///     <see cref="Severity" /> optionally overrides the descriptor's default severity (e.g. a rule that is
///     a warning by default but escalates to an error under strict lifetime safety).
/// </summary>
internal sealed record DiagnosticInfo(
	DiagnosticDescriptor Descriptor,
	LocationInfo? Location,
	EquatableArray<string> MessageArgs,
	DiagnosticSeverity? Severity = null)
{
	/// <summary>
	///     The severity this diagnostic is actually reported at: the <see cref="Severity" /> override when
	///     set, otherwise the descriptor's default.
	/// </summary>
	public DiagnosticSeverity EffectiveSeverity => Severity ?? Descriptor.DefaultSeverity;

	public Diagnostic ToDiagnostic()
	{
		object?[] args = MessageArgs.AsArray().Cast<object?>().ToArray();
		Location location = Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None;
		return Severity is { } severity
			? Diagnostic.Create(Descriptor, location, severity, additionalLocations: null, properties: null, args)
			: Diagnostic.Create(Descriptor, location, args);
	}
}
