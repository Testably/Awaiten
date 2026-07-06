using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Awaiten;

/// <summary>
///     Generic conveniences over the <see cref="IAwaitenResolver" /> resolution surface, so callers
///     with a compile-time type can write <c>resolver.Resolve&lt;T&gt;()</c> instead of casting the
///     result of <see cref="IAwaitenResolver.Resolve(Type)" />.
/// </summary>
public static class AwaitenResolverExtensions
{
	private static void ThrowIfNull(IAwaitenResolver resolver)
	{
		if (resolver is null)
		{
			throw new ArgumentNullException(nameof(resolver));
		}
	}

	/// <inheritdoc cref="AwaitenResolverExtensions" />
	extension(IAwaitenResolver resolver)
	{
		/// <summary>Resolves a service of type <typeparamref name="T" />, throwing if it is not registered.</summary>
		public T Resolve<T>()
		{
			ThrowIfNull(resolver);

			// Typed fast path: dispatches through a JIT-specialized generic check instead of the runtime
			// Type-keyed lookup. Relationship types (Func<T>/Lazy<T>) and unregistered services do not
			// implement IAwaitenResolver<T> and fall through to the Type-based path below.
			if (resolver is IAwaitenResolver<T> typed)
			{
				return typed.Resolve();
			}

			return (T)resolver.Resolve(typeof(T));
		}

		/// <summary>Attempts to resolve a service of type <typeparamref name="T" />.</summary>
		public bool TryResolve<T>([NotNullWhen(true)] out T? instance)
		{
			ThrowIfNull(resolver);

			if (resolver.TryResolve(typeof(T), out object? resolved))
			{
				instance = (T)resolved!;
				return true;
			}

			instance = default;
			return false;
		}

		/// <summary>
		///     Resolves the service of type <typeparamref name="T" /> registered under <paramref name="key" />,
		///     throwing if it is not registered. There is no typed fast path for a keyed resolution, so this always
		///     dispatches through the runtime <see cref="IAwaitenResolver.Resolve(Type, object)" />.
		/// </summary>
		public T Resolve<T>(string key)
		{
			ThrowIfNull(resolver);

			return (T)resolver.Resolve(typeof(T), key);
		}

		/// <summary>Attempts to resolve the service of type <typeparamref name="T" /> registered under <paramref name="key" />.</summary>
		public bool TryResolve<T>(string key, [NotNullWhen(true)] out T? instance)
		{
			ThrowIfNull(resolver);

			if (resolver.TryResolve(typeof(T), key, out object? resolved))
			{
				instance = (T)resolved!;
				return true;
			}

			instance = default;
			return false;
		}
	}

	/// <inheritdoc cref="AwaitenResolverExtensions" />
	extension(IAwaitenAsyncResolver resolver)
	{
		/// <summary>
		///     Resolves a service of type <typeparamref name="T" /> asynchronously, awaiting its
		///     <see cref="IAsyncInitializable.InitializeAsync" /> (and that of its non-deferred async
		///     dependencies) where required. For a service that needs no asynchronous initialization this
		///     completes synchronously. Throws if it is not registered.
		/// </summary>
		public Task<T> ResolveAsync<T>(CancellationToken cancellationToken = default)
		{
			// Validate eagerly, not deferred into the returned task. The await-and-cast lives in the local
			// async function below, which the synchronous body invokes only after the argument is checked.
			ThrowIfNull(resolver);

			return Cast(resolver.ResolveAsync(typeof(T), cancellationToken));

			static async Task<T> Cast(Task<object> resolution) => (T)await resolution.ConfigureAwait(false);
		}

		/// <summary>
		///     Resolves the service of type <typeparamref name="T" /> registered under <paramref name="key" />
		///     asynchronously, awaiting its <see cref="IAsyncInitializable.InitializeAsync" /> (and that of its
		///     non-deferred async dependencies) where required. Throws if it is not registered.
		/// </summary>
		public Task<T> ResolveAsync<T>(string key, CancellationToken cancellationToken = default)
		{
			ThrowIfNull(resolver);

			return Cast(resolver.ResolveAsync(typeof(T), key, cancellationToken));

			static async Task<T> Cast(Task<object> resolution) => (T)await resolution.ConfigureAwait(false);
		}
	}
}
