using System;
using System.Threading;
using System.Threading.Tasks;

namespace Awaiten;

/// <summary>
///     A resolution scope: the unit that resolves services, owns the instances it creates and can open
///     nested child scopes. The outermost scope (the generated <c>Root</c>) additionally owns the
///     singletons; every scope created from it - or from another scope - shares those singletons.
/// </summary>
/// <remarks>
///     Scoped registrations resolve to a single instance per scope; transients resolved from a scope are
///     owned by it. Disposing a scope disposes the instances it owns (its scoped instances and disposable
///     transients) in reverse order of creation. Disposing the root additionally disposes the singletons;
///     disposing a child scope does not.
/// </remarks>
public interface IAwaitenScope : IAwaitenResolver, IDisposable
{
	/// <summary>
	///     Creates a new child <see cref="IAwaitenScope" />. Scoped registrations resolve to a single
	///     instance per scope; disposing the scope disposes the instances it owns.
	/// </summary>
	IAwaitenScope CreateScope();

	/// <summary>
	///     Eagerly constructs and initializes, in dependency order, the async-initialized services this
	///     scope owns: the shared singletons on the root, and the scoped instances on a child scope.
	///     Idempotent and thread-safe - initialization of each instance runs at most once.
	/// </summary>
	Task InitializeAsync(CancellationToken cancellationToken = default);

	/// <summary>
	///     Creates a new child <see cref="IAwaitenScope" /> whose async-initialized scoped services have
	///     been eagerly constructed and initialized in dependency order.
	/// </summary>
	Task<IAwaitenScope> CreateScopeAsync(CancellationToken cancellationToken = default);
}
