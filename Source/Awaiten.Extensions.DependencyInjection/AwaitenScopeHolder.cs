using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Awaiten.Extensions.DependencyInjection;

/// <summary>
///     Aligns one <see cref="IAwaitenScope" /> of the container root <typeparamref name="TRoot" /> to each
///     Microsoft.Extensions.DependencyInjection scope. Generic over the root so several bridged containers keep
///     separate holders. Registered as MS.DI scoped, so one instance (and therefore one Awaiten scope) backs the
///     lifetime of each MS.DI scope.
/// </summary>
/// <remarks>
///     The holder disposes its Awaiten scope with the MS.DI scope, so everything the Awaiten scope built, nested
///     dependencies included, is disposed exactly once by Awaiten. It is resolved inside the first relaying
///     factory of its scope, so MS.DI captures it before any instance it relays and disposes those first.
/// </remarks>
#pragma warning disable S2326 // TRoot is a marker giving each root its own closed holder type, so MS.DI keeps a separate scoped registration per bridged container.
[SuppressMessage("Awaiten", "AWT135:Service locator: a resolver interface is injected into a service", Justification = "This is the MS.DI bridge adapter: aligning one IAwaitenScope to each MS.DI scope is the adaptation itself, not a hidden run-time dependency.")]
internal sealed class AwaitenScopeHolder<TRoot> : IDisposable, IAsyncDisposable
#pragma warning restore S2326
	where TRoot : class, IAwaitenContainerMetadata, new()
{
	public AwaitenScopeHolder(IAwaitenScope scope) => Scope = scope;

	public IAwaitenScope Scope { get; }

	/// <inheritdoc />
	public void Dispose() => Scope.Dispose();

	/// <inheritdoc />
	public ValueTask DisposeAsync()
	{
		if (Scope is IAsyncDisposable asyncDisposable)
		{
			return asyncDisposable.DisposeAsync();
		}

		Scope.Dispose();
		return default;
	}
}
