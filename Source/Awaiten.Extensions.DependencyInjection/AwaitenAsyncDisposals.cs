using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Awaiten.Extensions.DependencyInjection;

/// <summary>
///     Owns the disposal of instances resolved through the asynchronous <c>Task&lt;T&gt;</c> projection of
///     <see cref="AwaitenServiceCollectionExtensions.AddGeneratedContainer{TRoot}" />: MS.DI captures only
///     the <c>Task&lt;T&gt;</c> wrapper a factory returns - never the awaited instance - so the awaited
///     instances are tracked here and disposed when MS.DI disposes the tracker (registered as a singleton
///     for root-resolved services, and carried by the per-scope <c>AwaitenScopeHolder</c> for scope-resolved
///     ones).
/// </summary>
internal sealed class AwaitenAsyncDisposals : IDisposable, IAsyncDisposable
{
	private readonly List<object> _instances = new();

	/// <summary>
	///     Tracks an awaited instance for disposal. A disposable instance is tracked once by reference (an
	///     implementation awaited under several service types is disposed once); a non-disposable one is
	///     ignored.
	/// </summary>
	public void Track(object instance)
	{
		if (instance is not IDisposable && instance is not IAsyncDisposable)
		{
			return;
		}

		lock (_instances)
		{
			foreach (object existing in _instances)
			{
				if (ReferenceEquals(existing, instance))
				{
					return;
				}
			}

			_instances.Add(instance);
		}
	}

	/// <inheritdoc />
	public void Dispose()
	{
		lock (_instances)
		{
			for (int index = _instances.Count - 1; index >= 0; index--)
			{
				if (_instances[index] is IDisposable disposable)
				{
					disposable.Dispose();
				}
				else
				{
					// Mirrors the generated scope's synchronous drain: an instance that is only
					// IAsyncDisposable cannot be torn down synchronously.
					throw new InvalidOperationException(
						"Awaiten: a service resolved through the Task<T> projection requires asynchronous disposal (it implements IAsyncDisposable but not IDisposable); dispose the provider or scope with DisposeAsync ('await using') instead of a synchronous Dispose().");
				}
			}

			_instances.Clear();
		}
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		object[] instances;
		lock (_instances)
		{
			instances = _instances.ToArray();
			_instances.Clear();
		}

		for (int index = instances.Length - 1; index >= 0; index--)
		{
			if (instances[index] is IAsyncDisposable asyncDisposable)
			{
				await asyncDisposable.DisposeAsync().ConfigureAwait(false);
			}
			else
			{
				((IDisposable)instances[index]).Dispose();
			}
		}
	}
}
