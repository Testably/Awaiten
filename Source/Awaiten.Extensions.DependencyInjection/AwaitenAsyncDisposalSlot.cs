using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Awaiten.Extensions.DependencyInjection;

/// <summary>
///     Owns the disposal of one instance resolved through the asynchronous <c>Task&lt;T&gt;</c> projection of
///     <see cref="AwaitenServiceCollectionExtensions.AddGeneratedContainer{TRoot}(IServiceCollection)" />:
///     MS.DI captures only
///     the <c>Task&lt;T&gt;</c> wrapper a factory returns - never the awaited instance - so the factory
///     resolves one transient slot per resolution and fills it with the awaited instance. Because MS.DI
///     captures the slot at the position the <c>Task&lt;T&gt;</c> was resolved, its reverse-order teardown
///     disposes the awaited instance exactly where a natively registered instance would be disposed.
/// </summary>
internal sealed class AwaitenAsyncDisposalSlot : IDisposable, IAsyncDisposable
{
	private readonly object _lock = new();
	private object? _instance;
	private bool _disposed;

	/// <summary>
	///     Hands the awaited instance to the slot. A non-disposable instance is ignored. When the slot was
	///     already disposed (its scope was torn down while the resolution was still in flight), the late
	///     instance is disposed immediately instead of leaking.
	/// </summary>
	public ValueTask Fill(object instance)
	{
		if (instance is not IDisposable && instance is not IAsyncDisposable)
		{
			return default;
		}

		lock (_lock)
		{
			if (!_disposed)
			{
				_instance = instance;
				return default;
			}
		}

		if (instance is IAsyncDisposable asyncDisposable)
		{
			return asyncDisposable.DisposeAsync();
		}

		((IDisposable)instance).Dispose();
		return default;
	}

	/// <inheritdoc />
	public void Dispose()
	{
		object? instance;
		lock (_lock)
		{
			if (_disposed)
			{
				return;
			}

			if (_instance is IAsyncDisposable && _instance is not IDisposable)
			{
				// Mirrors the generated scope's synchronous drain. The slot stays filled, so a follow-up
				// DisposeAsync can still tear the instance down.
				throw new InvalidOperationException(
					"Awaiten: a service resolved through the Task<T> projection requires asynchronous disposal (it implements IAsyncDisposable but not IDisposable); dispose the provider or scope with DisposeAsync ('await using') instead of a synchronous Dispose().");
			}

			_disposed = true;
			instance = _instance;
			_instance = null;
		}

		(instance as IDisposable)?.Dispose();
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		object? instance;
		lock (_lock)
		{
			_disposed = true;
			instance = _instance;
			_instance = null;
		}

		if (instance is IAsyncDisposable asyncDisposable)
		{
			await asyncDisposable.DisposeAsync().ConfigureAwait(false);
		}
		else
		{
			(instance as IDisposable)?.Dispose();
		}
	}
}
