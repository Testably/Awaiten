using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators.Internals;

/// <summary>
///     An equatable description of a diagnostic to report, deferred until the source-output stage.
/// </summary>
internal sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Location, EquatableArray<string> MessageArgs)
{
	public Diagnostic ToDiagnostic()
		=> Diagnostic.Create(
			Descriptor,
			Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None,
			MessageArgs.AsArray().Cast<object?>().ToArray());
}