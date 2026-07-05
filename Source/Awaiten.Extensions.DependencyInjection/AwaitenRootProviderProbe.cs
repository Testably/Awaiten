using System;

namespace Awaiten.Extensions.DependencyInjection;

/// <summary>
///     Captures the provider its singleton factory executes against. MS.DI realizes singletons on the root
///     provider, so a transient factory (whose <c>sp</c> argument is the provider it resolves from) can tell
///     whether it is resolving from the root provider or from a scope.
/// </summary>
internal sealed class AwaitenRootProviderProbe
{
	public AwaitenRootProviderProbe(IServiceProvider rootProvider) => RootProvider = rootProvider;

	public IServiceProvider RootProvider { get; }
}
