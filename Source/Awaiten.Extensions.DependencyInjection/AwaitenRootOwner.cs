using System;
using System.Threading.Tasks;

namespace Awaiten.Extensions.DependencyInjection;

/// <summary>
///     Makes the MS.DI root provider the disposal owner of the container root <typeparamref name="TRoot" />.
///     Registered through a factory so MS.DI captures it, unlike the pre-built root, which MS.DI registers as a
///     constant and never disposes. Every bridge path that can make the root build an instance resolves this
///     owner first, so it is captured before anything the root builds and the root is drained after the
///     instances MS.DI disposes itself. A root the caller passed in stays the caller's to dispose, so its owner
///     only relays it.
/// </summary>
internal sealed class AwaitenRootOwner<TRoot> : IDisposable, IAsyncDisposable
	where TRoot : class, IAwaitenRoot
{
	private readonly bool _ownsRoot;

	public AwaitenRootOwner(TRoot root, bool ownsRoot)
	{
		Root = root;
		_ownsRoot = ownsRoot;
	}

	public TRoot Root { get; }

	/// <inheritdoc />
	public void Dispose()
	{
		if (_ownsRoot)
		{
			Root.Dispose();
		}
	}

	/// <inheritdoc />
	public ValueTask DisposeAsync()
	{
		if (!_ownsRoot)
		{
			return default;
		}

#if NET
		return Root.DisposeAsync();
#else
		if (Root is IAsyncDisposable asyncDisposable)
		{
			return asyncDisposable.DisposeAsync();
		}

		Root.Dispose();
		return default;
#endif
	}
}
