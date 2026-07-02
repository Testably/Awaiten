using System;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection;

/// <summary>
///     An <see cref="IServiceScope" /> backed by an <see cref="IAwaitenScope" />. Its
///     <see cref="ServiceProvider" /> is an <see cref="AwaitenServiceProvider" /> over that scope, so it
///     resolves from - and can open nested scopes off (<see cref="IServiceScopeFactory" />) - the Awaiten
///     scope; disposing it disposes the underlying Awaiten scope (and the scoped instances and disposable
///     transients it owns).
/// </summary>
internal sealed class AwaitenServiceScope : IServiceScope
{
	private readonly AwaitenServiceProvider _provider;

	public AwaitenServiceScope(IAwaitenScope scope) => _provider = new AwaitenServiceProvider(scope);

	public IServiceProvider ServiceProvider => _provider;

	public void Dispose() => _provider.Dispose();
}
