using System;

namespace Awaiten;

/// <summary>
///     A single external (host-owned) dependency a generated container expects to resolve through its
///     <see cref="IExternalResolverHost.ExternalResolver" />: the service type of a <c>[FromServices]</c> /
///     <c>[ImportServices]</c> constructor parameter and the optional resolution key requested by a
///     <c>[FromKey]</c> on it (<see langword="null" /> for an unkeyed dependency). A host verifies these
///     against its provider at startup; the <see cref="Key" /> selects the matching keyed registration.
/// </summary>
public readonly struct AwaitenExternalDependency : IEquatable<AwaitenExternalDependency>
{
	/// <summary>
	///     Initializes a new instance of the <see cref="AwaitenExternalDependency" /> struct.
	/// </summary>
	/// <param name="serviceType">The service type expected from the external provider.</param>
	/// <param name="key">The resolution key of the dependency, or <see langword="null" /> when it is unkeyed.</param>
	/// <exception cref="ArgumentNullException"><paramref name="serviceType" /> is <see langword="null" />.</exception>
	public AwaitenExternalDependency(Type serviceType, object? key = null)
	{
		ServiceType = serviceType ?? throw new ArgumentNullException(nameof(serviceType));
		Key = key;
	}

	/// <summary>
	///     The service type the container expects to resolve from the external provider.
	/// </summary>
	public Type ServiceType { get; }

	/// <summary>
	///     The resolution key requested by a <c>[FromKey]</c> on the dependency, or <see langword="null" />
	///     when the dependency is unkeyed.
	/// </summary>
	public object? Key { get; }

	/// <inheritdoc />
	public bool Equals(AwaitenExternalDependency other)
		=> ServiceType == other.ServiceType && Equals(Key, other.Key);

	/// <inheritdoc />
	public override bool Equals(object? obj) => obj is AwaitenExternalDependency other && Equals(other);

	/// <summary>
	///     Determines whether two <see cref="AwaitenExternalDependency" /> values are equal.
	/// </summary>
	public static bool operator ==(AwaitenExternalDependency left, AwaitenExternalDependency right) => left.Equals(right);

	/// <summary>
	///     Determines whether two <see cref="AwaitenExternalDependency" /> values are unequal.
	/// </summary>
	public static bool operator !=(AwaitenExternalDependency left, AwaitenExternalDependency right) => !left.Equals(right);

	/// <inheritdoc />
	public override int GetHashCode()
		=> unchecked((ServiceType.GetHashCode() * 397) ^ (Key?.GetHashCode() ?? 0));
}
