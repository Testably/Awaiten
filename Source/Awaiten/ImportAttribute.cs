using System;

namespace Awaiten;

/// <summary>
///     Imports a <see cref="ModuleAttribute">module</see>'s registrations into the container. The
///     container's own registrations take precedence over imported ones, and an imported overridable
///     <c>Default</c> / <c>TryAdd</c> registration is used only when nothing else provides the service.
///     Imports are resolved one level deep - a module's own <see cref="ImportAttribute" /> is not followed.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ImportAttribute : Attribute
{
	/// <summary>
	///     Initializes a new instance of the <see cref="ImportAttribute" /> class.
	/// </summary>
	/// <param name="module">The module type whose registrations are imported.</param>
	public ImportAttribute(Type module) => Module = module;

	/// <summary>
	///     The module type whose registrations are imported.
	/// </summary>
	public Type Module { get; }
}
