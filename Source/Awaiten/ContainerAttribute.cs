using System;

namespace Awaiten;

/// <summary>
///     Marks a <see langword="static" /> <see langword="partial" /> class as an Awaiten composition
///     root. The class is a pure definition: its registration attributes, plus any <c>static</c> factory
///     methods or pre-built instance members they name. The source generator emits the nested
///     <c>Scope</c> and <c>Root</c> types that implement resolution, scopes and disposal; the root is the
///     usable instance (<c>new MyContainer.Root()</c>).
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

	/// <summary>
	///     How strictly the container guards against unbounded disposal leaks. Defaults to
	///     <see cref="Awaiten.LifetimeSafety.Strict" />, which makes a root-accumulating
	///     <c>Func&lt;…&gt;</c> over a disposable service a compile-time error and withholds such services
	///     from by-type resolution; set <see cref="Awaiten.LifetimeSafety.Loose" /> for full
	///     Microsoft.Extensions.DependencyInjection interoperability.
	/// </summary>
	public LifetimeSafety LifetimeSafety { get; set; }
}
