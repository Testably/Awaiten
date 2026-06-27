using System;

namespace Awaiten;

/// <summary>
///     A resolution scope created from an <see cref="IAwaitenContainer" />. Scoped registrations
///     resolve to a single instance per scope; transients resolved from the scope are owned by it.
///     Disposing the scope disposes the instances it owns (scoped instances and disposable
///     transients) in reverse order of creation; it does not dispose the container's singletons.
/// </summary>
public interface IAwaitenScope : IDisposable
{
	/// <summary>
	///     Resolves a service of type <typeparamref name="T" /> from this scope, throwing if it is
	///     not registered.
	/// </summary>
	T Get<T>();

	/// <summary>
	///     Resolves a service of the given <paramref name="serviceType" /> from this scope,
	///     throwing if it is not registered.
	/// </summary>
	object Resolve(Type serviceType);

	/// <summary>
	///     Attempts to resolve a service of the given <paramref name="serviceType" /> from this scope.
	/// </summary>
	bool TryResolve(Type serviceType, out object? instance);
}
