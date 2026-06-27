using System;

namespace Awaiten;

/// <summary>
///     Marks a <see langword="partial" /> class as an Awaiten composition root.
///     The source generator emits the container implementation (resolution,
///     scopes and async initialization) for the registrations declared on it.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ContainerAttribute : Attribute
{
	/// <summary>
	///     Opt into pragmatic synchronous resolution of async-initialized services
	///     after <c>InitializeAsync</c> has completed. Defaults to <see langword="false" />
	///     (strict: async-tainted services must be resolved through <c>GetAsync</c>).
	/// </summary>
	public bool SyncResolveAfterInit { get; set; }
}
