using System.Threading;
using System.Threading.Tasks;

namespace Awaiten;

/// <summary>
///     Implemented by a service that requires asynchronous initialization once it has been
///     constructed (for example, opening a connection or performing a handshake). Awaiten calls
///     <see cref="InitializeAsync" /> exactly once per owned instance - after construction and before
///     the instance is handed out - through <c>ResolveAsync</c>, <c>InitializeAsync</c> or
///     <c>CreateScopeAsync</c>.
/// </summary>
/// <remarks>
///     A service whose implementation (or any of its non-deferred dependencies) implements this
///     interface is <em>async-tainted</em>: in the strict default it can only be obtained
///     asynchronously, and resolving it synchronously is a compile-time error (AWT119 / AWT120).
/// </remarks>
public interface IAsyncInitializable
{
	/// <summary>
	///     Performs the asynchronous initialization of this instance. Awaiten invokes it once, after
	///     the instance is constructed and its async dependencies are initialized.
	/// </summary>
	/// <param name="cancellationToken">A token that cancels the initialization.</param>
	Task InitializeAsync(CancellationToken cancellationToken);
}
