using System;
using System.Diagnostics.CodeAnalysis;

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
	}
}
