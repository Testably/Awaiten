using System;
using System.Threading.Tasks;

namespace Awaiten.Extensions.DependencyInjection;

/// <summary>
///     Aligns one <see cref="IAwaitenScope" /> of the container root <typeparamref name="TRoot" /> to each
///     Microsoft.Extensions.DependencyInjection scope (generic over the root, so several bridged containers
///     keep separate holders). Registered as MS.DI scoped, so a single instance - and therefore a single
///     Awaiten scope - backs the lifetime of each MS.DI scope.
/// </summary>
/// <remarks>
///     Disposing the holder deliberately does not dispose the Awaiten scope: the synchronously relayed
///     instances are disposed by the MS.DI scope they were resolved from, so disposing the Awaiten scope as
///     well would double-dispose them. Only the <see cref="AsyncDisposals" /> - instances awaited through the
///     <c>Task&lt;T&gt;</c> projection, of which MS.DI captures just the <c>Task</c> wrapper - are disposed
///     here. The Awaiten scope itself is collected with the holder when the MS.DI scope is torn down.
/// </remarks>
internal sealed class AwaitenScopeHolder<TRoot> : IDisposable, IAsyncDisposable
	where TRoot : class, IAwaitenContainerMetadata, new()
{
	public AwaitenScopeHolder(IAwaitenScope scope) => Scope = scope;

	public IAwaitenScope Scope { get; }

	public AwaitenAsyncDisposals AsyncDisposals { get; } = new AwaitenAsyncDisposals();

	public void Dispose() => AsyncDisposals.Dispose();

	public ValueTask DisposeAsync() => AsyncDisposals.DisposeAsync();
}
