using System;

namespace Awaiten;

/// <summary>
///     A scope that routes its <c>[ImportService&lt;T&gt;]</c> / <c>[ImportServices]</c> dependencies through an
///     <see cref="IExternalResolver" />. Every generated container root and child scope implements this.
///     A host (for example the <c>Awaiten.Extensions.DependencyInjection</c> bridge) calls
///     <see cref="SetExternalResolver" /> on each scope so external dependencies resolve from the provider
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
	IExternalResolver? ExternalResolver { get; }

	/// <summary>
	///     Sets the <see cref="ExternalResolver" /> of this scope. The resolver can be set once per scope, so one
	///     scope never mixes instances from two providers; setting the resolver it already holds again is a no-op.
	/// </summary>
	/// <param name="resolver">The resolver to route this scope's external dependencies through.</param>
	/// <exception cref="ArgumentNullException"><paramref name="resolver" /> is <see langword="null" />.</exception>
	/// <exception cref="InvalidOperationException">This scope already has a different resolver.</exception>
	void SetExternalResolver(IExternalResolver resolver);
}
