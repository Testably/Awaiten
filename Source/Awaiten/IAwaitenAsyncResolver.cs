using System;
using System.Threading;
using System.Threading.Tasks;

namespace Awaiten;

/// <summary>
///     The <see cref="IAwaitenResolver" /> surface extended with asynchronous resolution. A container and its
///     scopes implement this. A synchronous-only adapter (such as one bridging an <see cref="IServiceProvider" />
///     with no async concept) implements the plain <see cref="IAwaitenResolver" /> instead, so it is never obliged
///     to honor a contract it cannot fulfill. The generic <c>ResolveAsync&lt;T&gt;</c> convenience is an extension
///     method over this surface (<see cref="AwaitenResolverExtensions" />).
/// </summary>
public interface IAwaitenAsyncResolver : IAwaitenResolver
{
	/// <summary>
	///     Resolves a service of the given <paramref name="serviceType" /> asynchronously, awaiting the
	///     <see cref="IAsyncInitializable.InitializeAsync" /> of the service and its non-deferred async
	///     dependencies (each exactly once). A service that needs no asynchronous initialization completes
	///     synchronously. Throws if it is not registered. In the strict default this is the only way to obtain an
	///     async-tainted service.
	/// </summary>
	Task<object> ResolveAsync(Type serviceType, CancellationToken cancellationToken = default);
}
