using System;

namespace Awaiten;

/// <summary>
///     Imports a <see cref="ModuleAttribute">module</see>'s registrations into the container. The
///     container's own registrations take precedence over imported ones, and an imported overridable default
///     (<c>Fallback.Warn</c> / <c>Fallback.Silent</c>) registration is used only when nothing else provides the service.
///     Imports are resolved one level deep: a module carrying its own <see cref="ImportAttribute" /> is rejected
///     (AWT150). Import the nested module on the container directly.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ImportAttribute : Attribute
{
	/// <param name="module">The module type whose registrations are imported.</param>
	public ImportAttribute(Type module) => Module = module;

	/// <summary>The module type whose registrations are imported.</summary>
	public Type Module { get; }
}
