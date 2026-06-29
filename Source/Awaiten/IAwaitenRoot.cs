using System.Threading;
using System.Threading.Tasks;

namespace Awaiten;

/// <summary>
///     The composition root: the outermost <see cref="IAwaitenScope" /> (the generated <c>Root</c>, the
///     usable container instance) that owns the singletons. It is the only scope constructed directly
///     (<c>new MyContainer.Root()</c>) rather than through <see cref="IAwaitenScope.CreateScope" /> /
///     <see cref="IAwaitenScope.CreateScopeAsync" />, so it is the only one that exposes an explicit
///     <see cref="InitializeAsync" /> to warm itself; child scopes are warmed when they are created.
/// </summary>
public interface IAwaitenRoot : IAwaitenScope
{
	/// <summary>
	///     Eagerly constructs and initializes the async-initialized singletons in dependency order.
	///     Idempotent and thread-safe - initialization of each singleton runs at most once. Call this once at
	///     startup so later resolution hands back warmed singletons. (Scoped async services are warmed per
	///     scope by <see cref="IAwaitenScope.CreateScopeAsync" />.)
	/// </summary>
	Task InitializeAsync(CancellationToken cancellationToken = default);
}
