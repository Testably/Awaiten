using System;

namespace Awaiten.Extensions.DependencyInjection;

/// <summary>
///     Aligns one <see cref="IAwaitenScope" /> of the container root <typeparamref name="TRoot" /> to each
///     Microsoft.Extensions.DependencyInjection scope. Generic over the root so several bridged containers keep
///     separate holders. Registered as MS.DI scoped, so one instance (and therefore one Awaiten scope) backs the
///     lifetime of each MS.DI scope.
/// </summary>
/// <remarks>
///     The holder deliberately does not dispose the Awaiten scope. Synchronously relayed instances are disposed by
///     the MS.DI scope they were resolved from, so disposing the Awaiten scope too would double-dispose them.
///     An instance awaited through the <c>Task&lt;T&gt;</c> projection is disposed by the
///     <see cref="AwaitenAsyncDisposalSlot" /> captured with its resolution. The Awaiten scope is collected with
///     the holder when the MS.DI scope is torn down.
/// </remarks>
#pragma warning disable S2326 // TRoot is a marker giving each root its own closed holder type, so MS.DI keeps a separate scoped registration per bridged container.
internal sealed class AwaitenScopeHolder<TRoot>
#pragma warning restore S2326
	where TRoot : class, IAwaitenContainerMetadata, new()
{
	public AwaitenScopeHolder(IAwaitenScope scope) => Scope = scope;

	public IAwaitenScope Scope { get; }
}
