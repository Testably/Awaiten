namespace Awaiten;

/// <summary>
///     A scope that routes its <c>[FromServices]</c> / <c>[ImportServices]</c> dependencies through an
///     <see cref="IExternalResolver" />. Every generated container root and child scope implements this.
///     A host (for example the <c>Awaiten.Extensions.DependencyInjection</c> bridge) sets
///     <see cref="ExternalResolver" /> on each scope so external dependencies resolve from the provider
///     aligned to that scope. A child scope with no resolver of its own falls back to the root's.
/// </summary>
public interface IExternalResolverHost
{
	/// <summary>
	///     The external resolver this scope routes its <c>[FromServices]</c> / <c>[ImportServices]</c>
	///     dependencies through. Defaults to <see langword="null" />: a child scope then falls back to the root's
	///     resolver, and resolving an external dependency with neither set throws.
	/// </summary>
	IExternalResolver? ExternalResolver { get; set; }
}
