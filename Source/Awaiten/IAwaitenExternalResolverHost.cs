namespace Awaiten;

/// <summary>
///     A scope that routes its <c>[ImportService&lt;T&gt;]</c> / <c>[ImportServices]</c> dependencies through an
///     <see cref="IExternalResolver" />. Every generated container root and child scope implements this.
///     A host (for example the <c>Awaiten.Extensions.DependencyInjection</c> bridge) sets
///     <see cref="ExternalResolver" /> on each scope so external dependencies resolve from the provider
///     aligned to that scope. A child scope with no resolver of its own falls back to the root's. A standalone
///     container passes its resolver to the generated <c>Root(IExternalResolver?)</c> constructor instead.
/// </summary>
public interface IAwaitenExternalResolverHost
{
	/// <summary>
	///     The external resolver this scope routes its <c>[ImportService&lt;T&gt;]</c> / <c>[ImportServices]</c>
	///     dependencies through. Defaults to <see langword="null" />: a child scope then falls back to the root's
	///     resolver, and resolving an external dependency with neither set throws.
	/// </summary>
	/// <remarks>
	///     The resolver can be set once per scope, so one scope never mixes instances from two providers. Setting
	///     the resolver it already holds again is a no-op; setting a different one (or <see langword="null" />)
	///     once a resolver is set throws an <see cref="System.InvalidOperationException" />.
	/// </remarks>
	IExternalResolver? ExternalResolver { get; set; }
}
