using System;
using System.ComponentModel;

namespace Awaiten;

/// <summary>
///     The answer of <see cref="IAwaitenRoot.GetResolvability" />: whether the root resolves a service
///     synchronously and, when the container has the service but will not hand it over synchronously, why.
/// </summary>
/// <remarks>
///     The <see langword="default" /> value is <see cref="AwaitenResolvabilityStatus.NotRegistered" /> without a
///     reason.
/// </remarks>
public readonly struct AwaitenResolvability : IEquatable<AwaitenResolvability>
{
	/// <summary>Builds an answer. Called by generated container code.</summary>
	/// <param name="status">How the root answers the request.</param>
	/// <param name="reason">The message <c>Resolve</c> throws for a withheld service, or <see langword="null" />.</param>
	[EditorBrowsable(EditorBrowsableState.Never)]
	public AwaitenResolvability(AwaitenResolvabilityStatus status, string? reason)
	{
		Status = status;
		Reason = reason;
	}

	/// <summary>How the root answers the request. New values may be added, so handle unknown values with a default case.</summary>
	public AwaitenResolvabilityStatus Status { get; }

	/// <summary>
	///     Why the container has the service but will not hand it over synchronously, naming the service and what to
	///     change: the message <c>Resolve</c> throws, made available without throwing. Set for
	///     <see cref="AwaitenResolvabilityStatus.AsyncOnly" /> and <see cref="AwaitenResolvabilityStatus.WithheldOnRoot" />,
	///     otherwise <see langword="null" />.
	/// </summary>
	public string? Reason { get; }

	/// <inheritdoc />
	public bool Equals(AwaitenResolvability other) => Status == other.Status && Reason == other.Reason;

	/// <inheritdoc />
	public override bool Equals(object? obj) => obj is AwaitenResolvability other && Equals(other);

	/// <summary>Determines whether two <see cref="AwaitenResolvability" /> values are equal.</summary>
	public static bool operator ==(AwaitenResolvability left, AwaitenResolvability right) => left.Equals(right);

	/// <summary>Determines whether two <see cref="AwaitenResolvability" /> values are unequal.</summary>
	public static bool operator !=(AwaitenResolvability left, AwaitenResolvability right) => !left.Equals(right);

	/// <inheritdoc />
	public override int GetHashCode() => unchecked(((int)Status * 397) ^ (Reason?.GetHashCode() ?? 0));
}
