using System;

namespace Awaiten;

/// <summary>
///     The neutral resolution surface implemented by every generated container.
///     This is the dependency-free seam that the
///     <c>Awaiten.Extensions.DependencyInjection</c> companion package adapts to
///     <see cref="IServiceProvider" />.
/// </summary>
/// <remarks>
///     Disposing the container disposes the singletons it owns (and any disposables created while
///     building them), in reverse order of creation.
/// </remarks>
public interface IAwaitenContainer : IDisposable
{
	/// <summary>
	///     Resolves a service of type <typeparamref name="T" />, throwing if it is not registered.
	/// </summary>
	T Get<T>();

	/// <summary>
	///     Resolves a service of the given <paramref name="serviceType" />,
	///     throwing if it is not registered.
	/// </summary>
	object Resolve(Type serviceType);

	/// <summary>
	///     Attempts to resolve a service of the given <paramref name="serviceType" />.
	/// </summary>
	bool TryResolve(Type serviceType, out object? instance);

	/// <summary>
	///     Creates a new <see cref="IAwaitenScope" />. Scoped registrations resolve to a single
	///     instance per scope; disposing the scope disposes the instances it owns.
	/// </summary>
	IAwaitenScope CreateScope();
}
