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

// S2326: the type parameter is the Awaiten source generator's input - it reads the module type from the
// attribute's type argument via Roslyn symbols, so it is intentionally not referenced in the attribute body.
#pragma warning disable S2326

/// <summary>
///     Imports a <see cref="ModuleAttribute">module</see>'s registrations into the container - the typed
///     equivalent of <see cref="ImportAttribute" />, requiring no <c>typeof</c> because a module is a closed
///     type. The container's own registrations take precedence over imported ones, and an imported overridable
///     <c>Default</c> / <c>TryAdd</c> registration is used only when nothing else provides the service. Imports
///     are resolved one level deep - a module's own <c>[Import]</c> is not followed.
/// </summary>
/// <typeparam name="TModule">The module type whose registrations are imported.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ImportAttribute<TModule> : Attribute
	where TModule : class;

#pragma warning restore S2326
