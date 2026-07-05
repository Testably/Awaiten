using System;
using System.Diagnostics.CodeAnalysis;

namespace Awaiten;

/// <summary>
///     The neutral, dependency-free synchronous resolution surface shared by a container and its scopes: resolve
///     a service by its runtime <see cref="Type" />. The generic <c>Resolve&lt;T&gt;</c> and
///     <c>TryResolve&lt;T&gt;</c> conveniences are extension methods over it (<see cref="AwaitenResolverExtensions" />).
///     This is the seam the <c>Awaiten.Extensions.DependencyInjection</c> companion adapts to
///     <see cref="IServiceProvider" />. Asynchronous resolution lives on the derived
///     <see cref="IAwaitenAsyncResolver" />, so a synchronous-only adapter implements this minimal surface without
///     honoring an async contract it cannot fulfill.
/// </summary>
public interface IAwaitenResolver
{
	/// <summary>Resolves a service of the given <paramref name="serviceType" />, throwing if it is not registered.</summary>
	object Resolve(Type serviceType);

	/// <summary>Attempts to resolve a service of the given <paramref name="serviceType" />, reporting whether it could be resolved synchronously.</summary>
	/// <remarks>
	///     This probes synchronous resolvability, not mere registration. In the strict default an async-tainted
	///     service (one that is <see cref="IAsyncInitializable" /> or reaches one through its non-deferred
	///     dependencies) has no synchronous path, so this returns <see langword="false" /> even though the service
	///     is registered. Resolve it through <see cref="IAwaitenAsyncResolver.ResolveAsync" /> instead, or set
	///     <c>SyncResolveAfterInit</c> on the <c>[Container]</c> to make it synchronously resolvable after warm-up.
	/// </remarks>
	bool TryResolve(Type serviceType, [NotNullWhen(true)] out object? instance);
}
