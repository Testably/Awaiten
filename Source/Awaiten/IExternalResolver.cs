using System;

namespace Awaiten;

/// <summary>
///     The dependency-free seam through which a generated container resolves the dependencies it does
///     not own itself - its <c>[FromServices]</c> / <c>[ImportServices]</c> parameters - from an
///     external provider. The <c>Awaiten.Extensions.DependencyInjection</c> companion adapts a
///     Microsoft.Extensions.DependencyInjection <see cref="IServiceProvider" /> to it; a container used
///     standalone may implement it however it likes and assign it to
///     <see cref="IAwaitenContainerMetadata.ExternalResolver" />.
/// </summary>
public interface IExternalResolver
{
	/// <summary>
	///     Attempts to resolve a service of the given <paramref name="serviceType" /> from the external
	///     provider.
	/// </summary>
	/// <param name="serviceType">The service type to resolve.</param>
	/// <param name="instance">The resolved instance, or <see langword="null" /> when unavailable.</param>
	/// <returns><see langword="true" /> when the service was resolved; otherwise <see langword="false" />.</returns>
	bool TryResolve(Type serviceType, out object? instance);
}
