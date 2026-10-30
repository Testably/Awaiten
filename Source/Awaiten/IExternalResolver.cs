using System;
using System.Diagnostics.CodeAnalysis;

namespace Awaiten;

/// <summary>
///     The dependency-free seam through which a generated container resolves the dependencies it does not own (its
///     <c>[ImportService&lt;T&gt;]</c> / <c>[ImportServices]</c> dependencies) from an external provider. The
///     <c>Awaiten.Extensions.DependencyInjection</c> companion adapts a Microsoft.Extensions.DependencyInjection
///     <see cref="IServiceProvider" /> to it. A standalone container may implement it however it likes and pass
///     it to the generated <c>Root(IExternalResolver?)</c> constructor.
/// </summary>
public interface IExternalResolver
{
	/// <summary>
	///     Attempts to resolve a service of the given <paramref name="serviceType" /> from the external
	///     provider, optionally under a resolution key (from a <c>[FromKey]</c> on the parameter).
	/// </summary>
	/// <param name="serviceType">The service type to resolve.</param>
	/// <param name="key">
	///     The resolution key when the parameter carries a <c>[FromKey]</c>, or <see langword="null" /> for the
	///     unkeyed service.
	/// </param>
	/// <param name="instance">The resolved instance, or <see langword="null" /> when unavailable.</param>
	/// <returns><see langword="true" /> when the service was resolved; otherwise <see langword="false" />.</returns>
	bool TryResolve(Type serviceType, object? key, [NotNullWhen(true)] out object? instance);
}
