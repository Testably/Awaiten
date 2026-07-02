namespace Awaiten.Extensions.DependencyInjection;

/// <summary>
///     Aligns one <see cref="IAwaitenScope" /> to each Microsoft.Extensions.DependencyInjection scope.
///     Registered as MS.DI scoped, so a single instance - and therefore a single Awaiten scope - backs
///     the lifetime of each MS.DI scope.
/// </summary>
/// <remarks>
///     Deliberately not <see cref="System.IDisposable" />: the relayed instances are disposed by the
///     MS.DI scope they were resolved from, so disposing the Awaiten scope as well would double-dispose
///     them. The Awaiten scope is collected with the holder when the MS.DI scope is torn down.
/// </remarks>
internal sealed class AwaitenScopeHolder
{
	public AwaitenScopeHolder(IAwaitenScope scope) => Scope = scope;

	public IAwaitenScope Scope { get; }
}
