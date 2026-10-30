namespace Awaiten;

/// <summary>
///     How a container root answers a request for a service type and key, as reported by
///     <see cref="IAwaitenRoot.GetResolvability" />.
/// </summary>
/// <remarks>New values may be added in later versions, so handle unknown values with a default case.</remarks>
public enum AwaitenResolvabilityStatus
{
	/// <summary>The container has no registration for the service type under the key.</summary>
	NotRegistered = 0,

	/// <summary>The root resolves the service synchronously.</summary>
	Resolvable = 1,

	/// <summary>
	///     The container has the service but serves it only asynchronously, typically because it needs asynchronous
	///     initialization. <see cref="AwaitenResolvability.Reason" /> names how to obtain it.
	/// </summary>
	AsyncOnly = 2,

	/// <summary>
	///     The container has the service, but the root declines to build it, because the root would then track it for
	///     the container's lifetime (a disposable transient under the strict lifetime default, and the shapes over
	///     one). A child scope resolves it. <see cref="AwaitenResolvability.Reason" /> names the fix.
	/// </summary>
	WithheldOnRoot = 3,
}
