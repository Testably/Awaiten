using System;

namespace Awaiten;

/// <summary>
///     A single external (host-owned) dependency a generated container expects to resolve through its
///     <see cref="IExternalResolverHost.ExternalResolver" />: the service type of an <c>[ImportService&lt;T&gt;]</c> /
///     <c>[ImportServices]</c> dependency and the optional <c>[FromKey]</c> resolution key
///     (<see langword="null" /> when unkeyed). A host verifies these against its provider at startup.
/// </summary>
public readonly struct AwaitenExternalDependency : IEquatable<AwaitenExternalDependency>
{
	/// <param name="serviceType">The service type expected from the external provider.</param>
	/// <param name="key">The resolution key of the dependency, or <see langword="null" /> when it is unkeyed.</param>
	/// <exception cref="ArgumentNullException"><paramref name="serviceType" /> is <see langword="null" />.</exception>
	public AwaitenExternalDependency(Type serviceType, object? key = null)
	{
		ServiceType = serviceType ?? throw new ArgumentNullException(nameof(serviceType));
		Key = key;
	}

	/// <summary>The service type the container expects to resolve from the external provider.</summary>
	public Type ServiceType { get; }

	/// <summary>The <c>[FromKey]</c> resolution key requested by the dependency, or <see langword="null" /> when unkeyed.</summary>
	public object? Key { get; }

	/// <inheritdoc />
	public bool Equals(AwaitenExternalDependency other)
		=> ServiceType == other.ServiceType && Equals(Key, other.Key);

	/// <inheritdoc />
	public override bool Equals(object? obj) => obj is AwaitenExternalDependency other && Equals(other);

	/// <summary>Determines whether two <see cref="AwaitenExternalDependency" /> values are equal.</summary>
	public static bool operator ==(AwaitenExternalDependency left, AwaitenExternalDependency right) => left.Equals(right);

	/// <summary>Determines whether two <see cref="AwaitenExternalDependency" /> values are unequal.</summary>
	public static bool operator !=(AwaitenExternalDependency left, AwaitenExternalDependency right) => !left.Equals(right);

	/// <inheritdoc />
	public override int GetHashCode()
		=> unchecked((ServiceType.GetHashCode() * 397) ^ (Key?.GetHashCode() ?? 0));
}
