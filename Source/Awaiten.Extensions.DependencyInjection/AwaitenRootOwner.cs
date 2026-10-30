using System;
using System.Threading.Tasks;

namespace Awaiten.Extensions.DependencyInjection;

/// <summary>
///     Makes the MS.DI root provider the disposal owner of the container root <typeparamref name="TRoot" />.
///     Registered through a factory so MS.DI captures it, unlike the pre-built root, which MS.DI registers as a
///     constant and never disposes. Every bridge path that can make the root build an instance resolves this
///     owner first, so it is captured before anything the root builds and the root is drained after the
///     instances MS.DI disposes itself.
/// </summary>
internal sealed class AwaitenRootOwner<TRoot> : IDisposable, IAsyncDisposable
	where TRoot : class, IAwaitenContainerMetadata
{
	public AwaitenRootOwner(TRoot root) => Root = root;

	public TRoot Root { get; }

	/// <inheritdoc />
	public void Dispose() => Root.Dispose();

	/// <inheritdoc />
	public ValueTask DisposeAsync()
	{
		if (Root is IAsyncDisposable asyncDisposable)
		{
			return asyncDisposable.DisposeAsync();
		}

		Root.Dispose();
		return default;
	}
}
