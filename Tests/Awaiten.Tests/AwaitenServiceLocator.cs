using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Awaiten.Tests;

/// <summary>Stands in for the single-method resolution interface a non-MS.DI framework declares.</summary>
public interface IServiceLocator
{
	object? Resolve(Type? type);
}

/// <summary>
///     The adapter documented under AWT135, compiled so the documentation cannot drift from it. Keep this and the
///     sample in <c>Docs/pages/09-diagnostics.md</c> the same.
/// </summary>
[SuppressMessage("Awaiten", "AWT135:Service locator: a resolver interface is injected into a service",
	Justification = "This is a custom bridge adapter: holding the scope is the adaptation itself, not a hidden run-time dependency.")]
public sealed class AwaitenServiceLocator(IAwaitenContainerMetadata container, IAwaitenScope scope) : IServiceLocator
{
	public object? Resolve(Type? type)
	{
		if (type is null)
		{
			return null;
		}

		if (scope.TryResolve(type, out object? instance))
		{
			return instance;
		}

		// Reported as absent, the framework would bind it from elsewhere and fail far from the cause.
		if (container.WithheldReason(type, null) is { } reason)
		{
			throw new InvalidOperationException(reason);
		}

		return EmptyCollectionElement(type) is { } elementType
			? Array.CreateInstance(elementType, 0)
			: null;
	}

	/// <summary>
	///     The element type to answer with an empty sequence rather than <see langword="null" />, because a
	///     framework uses a collection as an extension point and "no handlers" still has to enumerate. A value
	///     element would need an array type native AOT does not generate; a resolvable one has members an empty
	///     sequence would drop.
	/// </summary>
	private Type? EmptyCollectionElement(Type type)
	{
		if (!type.IsConstructedGenericType || type.GetGenericTypeDefinition() != typeof(IEnumerable<>))
		{
			return null;
		}

		Type elementType = type.GenericTypeArguments[0];
		return elementType.IsValueType || container.IsResolvable(elementType, null) ? null : elementType;
	}
}
