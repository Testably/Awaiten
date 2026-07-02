using System;

namespace Awaiten;

/// <summary>
///     A single public registration advertised by a generated container through
///     <see cref="IAwaitenContainerMetadata" />: the service type that can be resolved, the lifetime under
///     which the container owns it, and whether it requires asynchronous resolution. The
///     <c>Awaiten.Extensions.DependencyInjection</c> companion projects these into a
///     Microsoft.Extensions.DependencyInjection service collection so a host can resolve Awaiten-owned
///     services.
/// </summary>
/// <remarks>
///     Only unkeyed service types are advertised; keyed registrations are reachable solely through
///     <c>[FromKey]</c> injection inside the Awaiten graph and have no place in the public dispatch. A service
///     that requires asynchronous resolution (<see cref="RequiresAsync" />) has no synchronous resolution
///     path, so it is projected as a <c>Task&lt;T&gt;</c> rather than a bare <c>T</c>.
/// </remarks>
public readonly struct AwaitenRegistration : IEquatable<AwaitenRegistration>
{
	/// <summary>
	///     Initializes a new instance of the <see cref="AwaitenRegistration" /> struct.
	/// </summary>
	/// <param name="serviceType">The service type that can be resolved.</param>
	/// <param name="lifetime">The lifetime under which the container owns the service.</param>
	/// <param name="requiresAsync">
	///     Whether the service must be resolved asynchronously (through <c>ResolveAsync</c>) - it is
	///     <c>IAsyncInitializable</c>, produced by an async factory, or depends on one.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="serviceType" /> is <see langword="null" />.</exception>
	public AwaitenRegistration(Type serviceType, AwaitenLifetime lifetime, bool requiresAsync = false)
	{
		ServiceType = serviceType ?? throw new ArgumentNullException(nameof(serviceType));
		Lifetime = lifetime;
		RequiresAsync = requiresAsync;
	}

	/// <summary>
	///     The service type that the container can resolve.
	/// </summary>
	public Type ServiceType { get; }

	/// <summary>
	///     The lifetime under which the container owns the service.
	/// </summary>
	public AwaitenLifetime Lifetime { get; }

	/// <summary>
	///     Whether the service must be resolved asynchronously - it is <c>IAsyncInitializable</c>, is produced
	///     by an asynchronous factory, or reaches one through its non-deferred dependencies - so it has no
	///     synchronous resolution path and is projected as a <c>Task&lt;T&gt;</c>.
	/// </summary>
	public bool RequiresAsync { get; }

	/// <inheritdoc />
	public bool Equals(AwaitenRegistration other)
		=> ServiceType == other.ServiceType && Lifetime == other.Lifetime && RequiresAsync == other.RequiresAsync;

	/// <inheritdoc />
	public override bool Equals(object? obj) => obj is AwaitenRegistration other && Equals(other);

	/// <inheritdoc />
	public override int GetHashCode()
		=> unchecked(((ServiceType.GetHashCode() * 397) ^ (int)Lifetime) * 397 ^ (RequiresAsync ? 1 : 0));
}
