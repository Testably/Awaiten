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
	/// <remarks>
	///     Limitation: this is reported per registration, not per resolution site - the generator does not
	///     track where a transient is resolved. It therefore fires for every disposable transient, even one
	///     only ever consumed as a dependency of a scoped service (where it is released with that scope). It
	///     is a warning precisely because the advice is advisory: the root is always a possible resolution
	///     site, so accumulation is possible, but it only actually happens if the root resolves it.
	/// </remarks>
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

	/// <summary>
	///     A <c>Factory</c> registration names a member that is not a usable factory method on the
	///     container (it is missing, is not a method, or does not return the registered service type).
	/// </summary>
	public static readonly DiagnosticDescriptor InvalidFactory = new(
		"AWT108",
		"Invalid factory",
		"'{0}' cannot be produced: the container has no accessible method '{1}' returning '{0}'",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	///     An <c>Instance</c> registration names a member that is not a usable instance member on the
	///     container (it is missing, is not a field or property, or is not assignable to the service type).
	/// </summary>
	public static readonly DiagnosticDescriptor InvalidInstance = new(
		"AWT109",
		"Invalid instance member",
		"'{0}' cannot be exposed: the container has no accessible field or property '{1}' of type '{0}'",
		"Awaiten",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);
}
