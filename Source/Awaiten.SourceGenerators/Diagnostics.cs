using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

/// <summary>
///     The diagnostics reported by the <see cref="AwaitenGenerator" />. Stable <c>AWK</c> ids in the
///     <c>Awaiten</c> category.
/// </summary>
internal static class Diagnostics
{
	/// <summary>
	///     A required dependency (a constructor parameter of a registered type) has no registration on
	///     the container.
	/// </summary>
	public static readonly DiagnosticDescriptor MissingDependency = new(
		"AWT101",
		"Missing dependency",
		"'{0}' cannot be resolved: '{1}' requires '{2}', which is not registered on the container",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A dependency cycle exists in the object graph.
	/// </summary>
	public static readonly DiagnosticDescriptor DependencyCycle = new(
		"AWT102",
		"Dependency cycle",
		"Dependency cycle detected: {0}",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);
}
