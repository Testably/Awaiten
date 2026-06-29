using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Awaiten;

/// <summary>
///     Generic conveniences over the <see cref="IAwaitenResolver" /> resolution surface, so callers
///     with a compile-time type can write <c>resolver.Resolve&lt;T&gt;()</c> instead of casting the
///     result of <see cref="IAwaitenResolver.Resolve" />.
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
		/// <summary>
		///     Resolves a service of type <typeparamref name="T" />, throwing if it is not registered.
		/// </summary>
		public T Resolve<T>()
		{
			ThrowIfNull(resolver);

			// Typed fast path: a generated container/scope implements IAwaitenResolver<T> for each
			// registered service type, so resolving a compile-time type dispatches through a
			// JIT-specialized generic check instead of the runtime Type-keyed lookup. Relationship types
			// (Func<T>/Lazy<T>) and unregistered services are not implemented as IAwaitenResolver<T> and
			// fall through to the Type-based path below.
			if (resolver is IAwaitenResolver<T> typed)
			{
				return typed.Resolve();
			}

			return (T)resolver.Resolve(typeof(T));
		}

		/// <summary>
		///     Attempts to resolve a service of type <typeparamref name="T" />.
		/// </summary>
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
		///     Resolves a service of type <typeparamref name="T" /> asynchronously, awaiting its
		///     <see cref="IAsyncInitializable.InitializeAsync" /> (and that of its non-deferred async
		///     dependencies) where required. For a service that needs no asynchronous initialization this
		///     completes synchronously. Throws if it is not registered.
		/// </summary>
		public async Task<T> ResolveAsync<T>(CancellationToken cancellationToken = default)
		{
			ThrowIfNull(resolver);

			return (T)await resolver.ResolveAsync(typeof(T), cancellationToken).ConfigureAwait(false);
		}
	}
}
