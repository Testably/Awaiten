using Microsoft.CodeAnalysis;

namespace Awaiten.SourceGenerators;

/// <summary>
///     The diagnostics reported by the <see cref="AwaitenGenerator" />. Stable <c>AWT</c> ids in the
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

	/// <summary>
	///     A registered implementation type cannot be instantiated (it is abstract or an interface).
	/// </summary>
	public static readonly DiagnosticDescriptor NotInstantiable = new(
		"AWT103",
		"Implementation is not instantiable",
		"'{0}' cannot be used as an implementation: it is abstract or an interface. Register a concrete type.",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A registered implementation type has no constructor the container can call.
	/// </summary>
	public static readonly DiagnosticDescriptor NoAccessibleConstructor = new(
		"AWT104",
		"No accessible constructor",
		"'{0}' has no accessible constructor for the container to call",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A singleton depends, directly or transitively through transients, on a shorter-lived scoped
	///     service, which it would capture for the container's lifetime.
	/// </summary>
	public static readonly DiagnosticDescriptor CaptiveDependency = new(
		"AWT105",
		"Captive dependency",
		"Captive dependency: the singleton '{0}' depends on '{1}', which has the shorter Scoped lifetime and would be captured for the container's lifetime",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     A disposable transient resolved from the container root is not released until the container
	///     is disposed, so such instances accumulate; resolving it from a scope releases it with the scope.
	/// </summary>
	public static readonly DiagnosticDescriptor DisposableTransient = new(
		"AWT106",
		"Disposable transient",
		"The transient '{0}' is disposable; instances resolved from the container root are not released until the container is disposed and so accumulate - resolve it from a scope instead",
		"Awaiten",
		DiagnosticSeverity.Warning,
		isEnabledByDefault: true);

	/// <summary>
	///     The same implementation is registered with more than one lifetime; coalescing into a single
	///     instance would silently drop one of the declared lifetimes.
	/// </summary>
	public static readonly DiagnosticDescriptor ConflictingLifetime = new(
		"AWT107",
		"Conflicting lifetime",
		"'{0}' is registered with conflicting lifetimes: {1} and {2}. Register the implementation with a single lifetime.",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);
}
