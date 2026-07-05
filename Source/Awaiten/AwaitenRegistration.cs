using System;
using System.Threading.Tasks;

namespace Awaiten;

/// <summary>
///     A single public registration advertised by a generated container through
///     <see cref="IAwaitenContainerMetadata" />: the service type, its lifetime, and whether it requires
///     asynchronous resolution. The <c>Awaiten.Extensions.DependencyInjection</c> companion projects these into a
///     Microsoft.Extensions.DependencyInjection service collection so a host can resolve Awaiten-owned services.
/// </summary>
/// <remarks>
///     Only unkeyed service types are advertised; keyed registrations are reachable only through <c>[FromKey]</c>
///     inside the Awaiten graph. A service that requires asynchronous resolution (<see cref="RequiresAsync" />)
///     has no synchronous path, so it is projected as a <c>Task&lt;T&gt;</c> rather than a bare <c>T</c>.
/// </remarks>
public readonly struct AwaitenRegistration : IEquatable<AwaitenRegistration>
{
	/// <summary>Builds a synchronous registration (<see cref="RequiresAsync" /> is <see langword="false" />).</summary>
	/// <param name="serviceType">The service type that can be resolved.</param>
	/// <param name="lifetime">The lifetime under which the container owns the service.</param>
	/// <param name="externallyOwned">Whether the instance is a pre-built member the container exposes but does not own.</param>
	/// <exception cref="ArgumentNullException"><paramref name="serviceType" /> is <see langword="null" />.</exception>
	public AwaitenRegistration(Type serviceType, AwaitenLifetime lifetime, bool externallyOwned = false)
	{
		ServiceType = serviceType ?? throw new ArgumentNullException(nameof(serviceType));
		Lifetime = lifetime;
		RequiresAsync = false;
		ExternallyOwned = externallyOwned;
		AsyncTaskType = null;
		AsyncTaskConverter = null;
	}

	/// <summary>
	///     Builds an asynchronous registration (<see cref="RequiresAsync" /> is <see langword="true" />), carrying
	///     the closed generics the bridge needs to project it as a <c>Task&lt;T&gt;</c> without reflection. Emitted
	///     by the generator, which knows the service type at compile time.
	/// </summary>
	/// <param name="serviceType">The service type that can be resolved.</param>
	/// <param name="lifetime">The lifetime under which the container owns the service.</param>
	/// <param name="asyncTaskType">The <c>typeof(Task&lt;TService&gt;)</c> the service is projected under.</param>
	/// <param name="asyncTaskConverter">
	///     A delegate bound to the closed <see cref="AwaitenTaskProjection.AsTask{T}" /> that adapts the container's
	///     <c>Task&lt;object&gt;</c> resolution to the <c>Task&lt;TService&gt;</c> the host asks for.
	/// </param>
	/// <exception cref="ArgumentNullException">Any argument is <see langword="null" />.</exception>
	public AwaitenRegistration(Type serviceType, AwaitenLifetime lifetime, Type asyncTaskType, Func<Task<object>, object> asyncTaskConverter)
	{
		ServiceType = serviceType ?? throw new ArgumentNullException(nameof(serviceType));
		Lifetime = lifetime;
		RequiresAsync = true;
		ExternallyOwned = false;
		AsyncTaskType = asyncTaskType ?? throw new ArgumentNullException(nameof(asyncTaskType));
		AsyncTaskConverter = asyncTaskConverter ?? throw new ArgumentNullException(nameof(asyncTaskConverter));
	}

	/// <summary>The service type that the container can resolve.</summary>
	public Type ServiceType { get; }

	/// <summary>The lifetime under which the container owns the service.</summary>
	public AwaitenLifetime Lifetime { get; }

	/// <summary>
	///     Whether the service must be resolved asynchronously. It is <c>IAsyncInitializable</c>, is produced by an
	///     async factory, or reaches one through its non-deferred dependencies, so it has no synchronous path and is
	///     projected as a <c>Task&lt;T&gt;</c>.
	/// </summary>
	public bool RequiresAsync { get; }

	/// <summary>
	///     Whether the instance is a pre-built member of the container (registered with <c>Instance</c>) that the
	///     container exposes but does not own. A host projecting the registration must not assume ownership of its disposal.
	/// </summary>
	public bool ExternallyOwned { get; }

	/// <summary>
	///     For an asynchronous registration, the <c>typeof(Task&lt;TService&gt;)</c> the bridge registers it under,
	///     or <see langword="null" /> otherwise. Derived from <see cref="ServiceType" />, so it takes no part in value equality.
	/// </summary>
	public Type? AsyncTaskType { get; }

	/// <summary>
	///     For an asynchronous registration, a delegate bound to the closed <see cref="AwaitenTaskProjection.AsTask{T}" />
	///     that adapts the container's <c>Task&lt;object&gt;</c> resolution to a <c>Task&lt;TService&gt;</c>, or
	///     <see langword="null" /> otherwise. Derived from <see cref="ServiceType" />, so it takes no part in value equality.
	/// </summary>
	public Func<Task<object>, object>? AsyncTaskConverter { get; }

	/// <inheritdoc />
	public bool Equals(AwaitenRegistration other)
		=> ServiceType == other.ServiceType && Lifetime == other.Lifetime && RequiresAsync == other.RequiresAsync
		   && ExternallyOwned == other.ExternallyOwned;

	/// <inheritdoc />
	public override bool Equals(object? obj) => obj is AwaitenRegistration other && Equals(other);

	/// <summary>Determines whether two <see cref="AwaitenRegistration" /> values are equal.</summary>
	public static bool operator ==(AwaitenRegistration left, AwaitenRegistration right) => left.Equals(right);

	/// <summary>Determines whether two <see cref="AwaitenRegistration" /> values are unequal.</summary>
	public static bool operator !=(AwaitenRegistration left, AwaitenRegistration right) => !left.Equals(right);

	/// <inheritdoc />
	public override int GetHashCode()
		=> unchecked((((ServiceType.GetHashCode() * 397) ^ (int)Lifetime) * 397 ^ (RequiresAsync ? 1 : 0)) * 397
		             ^ (ExternallyOwned ? 1 : 0));
}
