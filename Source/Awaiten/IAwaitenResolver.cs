using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

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
	///     Attempts to resolve a service of the given <paramref name="serviceType" />, reporting whether it
	///     could be resolved <em>synchronously</em>.
	/// </summary>
	/// <remarks>
	///     This probes synchronous resolvability, not mere registration. In the strict default an
	///     async-tainted service (one that is <see cref="IAsyncInitializable" />, or reaches one through its
	///     non-deferred dependencies) has no synchronous resolution path, so this returns
	///     <see langword="false" /> for it even though it is registered - resolve it through
	///     <see cref="ResolveAsync" /> instead (or set <c>SyncResolveAfterInit</c> on the <c>[Container]</c> to
	///     make it synchronously resolvable after warm-up).
	/// </remarks>
	bool TryResolve(Type serviceType, [NotNullWhen(true)] out object? instance);

	/// <summary>
	///     Resolves a service of the given <paramref name="serviceType" /> asynchronously, awaiting the
	///     <see cref="IAsyncInitializable.InitializeAsync" /> of the service and of its non-deferred async
	///     dependencies (each exactly once). For a service that needs no asynchronous initialization this
	///     completes synchronously. Throws if it is not registered. In the strict default this is the only
	///     way to obtain an async-tainted service.
	/// </summary>
	Task<object> ResolveAsync(Type serviceType, CancellationToken cancellationToken = default);
}
