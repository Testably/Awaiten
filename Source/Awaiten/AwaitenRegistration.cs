using System;

namespace Awaiten;

/// <summary>
///     A single public registration advertised by a generated container through
///     <see cref="IAwaitenContainerMetadata" />: the service type that can be resolved and the lifetime
///     under which the container owns it. The <c>Awaiten.Extensions.DependencyInjection</c> companion
///     projects these into a Microsoft.Extensions.DependencyInjection service collection so a host can
///     resolve Awaiten-owned services.
/// </summary>
/// <remarks>
///     Only unkeyed service types are advertised; keyed registrations are reachable solely through
///     <c>[FromKey]</c> injection inside the Awaiten graph and have no place in the public dispatch.
/// </remarks>
public readonly struct AwaitenRegistration : IEquatable<AwaitenRegistration>
{
	/// <summary>
	///     Initializes a new instance of the <see cref="AwaitenRegistration" /> struct.
	/// </summary>
	/// <param name="serviceType">The service type that can be resolved.</param>
	/// <param name="lifetime">The lifetime under which the container owns the service.</param>
	public AwaitenRegistration(Type serviceType, AwaitenLifetime lifetime)
	{
		ServiceType = serviceType;
		Lifetime = lifetime;
	}

	/// <summary>
	///     The service type that the container can resolve.
	/// </summary>
	public Type ServiceType { get; }

	/// <summary>
	///     The lifetime under which the container owns the service.
	/// </summary>
	public AwaitenLifetime Lifetime { get; }

	/// <inheritdoc />
	public bool Equals(AwaitenRegistration other) => ServiceType == other.ServiceType && Lifetime == other.Lifetime;

	/// <inheritdoc />
	public override bool Equals(object? obj) => obj is AwaitenRegistration other && Equals(other);

	/// <inheritdoc />
	public override int GetHashCode() => unchecked((ServiceType?.GetHashCode() ?? 0) * 397 ^ (int)Lifetime);
}
