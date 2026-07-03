using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection;

/// <summary>
///     An <see cref="IServiceScope" /> backed by an <see cref="IAwaitenScope" />. Its
///     <see cref="ServiceProvider" /> is an <see cref="AwaitenServiceProvider" /> over that scope, so it
///     resolves from - and can open nested scopes off (<see cref="IServiceScopeFactory" />) - the Awaiten
///     scope; disposing it disposes the underlying Awaiten scope (and the scoped instances and disposable
///     transients it owns). Hosts that tear scopes down asynchronously reach the scope's
///     <c>DisposeAsync</c> through <see cref="IAsyncDisposable" />.
/// </summary>
internal sealed class AwaitenServiceScope : IServiceScope, IAsyncDisposable
{
	private readonly AwaitenServiceProvider _provider;

	public AwaitenServiceScope(AwaitenServiceProvider provider) => _provider = provider;

	public IServiceProvider ServiceProvider => _provider;

	public void Dispose() => _provider.Dispose();

	public ValueTask DisposeAsync() => _provider.DisposeAsync();
}
