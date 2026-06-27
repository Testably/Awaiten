using System;

namespace Awaiten;

/// <summary>
///     The neutral resolution surface implemented by every generated container.
///     This is the dependency-free seam that the
///     <c>Awaiten.Extensions.DependencyInjection</c> companion package adapts to
///     <see cref="IServiceProvider" />.
/// </summary>
public interface IAwaitenContainer
{
	/// <summary>
	///     Resolves a service of the given <paramref name="serviceType" />,
	///     throwing if it is not registered.
	/// </summary>
	object Resolve(Type serviceType);

	/// <summary>
	///     Attempts to resolve a service of the given <paramref name="serviceType" />.
	/// </summary>
	bool TryResolve(Type serviceType, out object? instance);
}
