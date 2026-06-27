using System;

namespace Awaiten;

/// <summary>
///     The neutral, dependency-free resolution surface shared by a container and its scopes: resolve
///     a service by its runtime <see cref="Type" />. The generic <c>Resolve&lt;T&gt;</c> and
///     <c>TryResolve&lt;T&gt;</c> conveniences are extension methods over this surface
///     (<see cref="AwaitenResolverExtensions" />). This is the seam that the
///     <c>Awaiten.Extensions.DependencyInjection</c> companion package adapts to
///     <see cref="IServiceProvider" />.
/// </summary>
public interface IAwaitenResolver
{
	/// <summary>
	///     Resolves a service of the given <paramref name="serviceType" />, throwing if it is not
	///     registered.
	/// </summary>
	object Resolve(Type serviceType);

	/// <summary>
	///     Attempts to resolve a service of the given <paramref name="serviceType" />.
	/// </summary>
	bool TryResolve(Type serviceType, out object? instance);
}
